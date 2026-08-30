# HarmonyMetrics

InfluxDB metrics Harmony mod for this server. Based on [RustyMoose/Rust.ServerMetrics](https://github.com/RustyMoose/Rust.ServerMetrics) (August 2026 fork of the archived Pinkstink/features-not-bugs project), rewritten for a **Harmony-only** stack.

The original `oxide_plugins` / `Oxide.Core` hook-time collector is gone. This server does not run Oxide, so that path never produced data. HarmonyMetrics watches **loaded Harmony mods** instead and also samples Facepunch's built-in performance counters that the stock game already maintains.

## InfluxDB + Grafana (this machine)

| Service | Path | URL |
|---------|------|-----|
| InfluxDB 1.8 | `C:\InfluxDB-1.8` | `http://127.0.0.1:8086` |
| Grafana 12 | `C:\Grafana` | `http://127.0.0.1:3000` |

Quick start (desktop shortcuts `Influx.lnk` / `grafana.lnk`, or):

```powershell
powershell -NoExit -ExecutionPolicy Bypass -File C:\svr1\.cursor\HarmonyMods\HarmonyMetrics\tools\Start-InfluxDB.ps1
powershell -NoExit -ExecutionPolicy Bypass -File C:\svr1\.cursor\HarmonyMods\HarmonyMetrics\tools\Start-Grafana.ps1
```

Each shortcut opens a PowerShell launcher that stays open, plus a console window for the service itself. Do not use a relative `-File` path from `System32` or the window will flash closed.

Or use the elevated launchers under `C:\Rust-Server-Metrics-master\Start-InfluxDB-AsAdmin.bat` / `Start-Grafana-AsAdmin.bat` (those scripts now prefer `C:\InfluxDB-1.8` and `C:\Grafana`).

Database expected by config: `rust_server_metrics` (underscore). Grafana default login is `admin` / `admin` on first open.

**Never load this mod while Influx is down on an older build** — UnityWebRequest retries with a 15s timeout could freeze the dedicated process. Current builds pause uploads for 30s and drop buffered points when Influx is unreachable.

After replacing the DLL on a live server:

```text
harmony.unload HarmonyMetrics
harmony.load HarmonyMetrics
harmonymetrics.status
```

Do not replace the DLL mid-load without unload.

## What it samples

| Source | Measurement | Notes |
|--------|-------------|--------|
| `Performance.FPSTimer` | `framerate`, `frametime`, `memory`, `tasks`, `network`, `players`, `entities`, `npc_census` | Same core set as Rust.ServerMetrics. `players` includes sleepers/bots plus `bots_vanilla` / `bots_mod` / Grimm/Zombie/PersonalNPC splits when census is on. `npc_census` adds vanilla subtype + animal breakdown. |
| `PerformanceMetrics` / `Performance.Tick.performanceSample` | `cpu_sample` | Per-second Unity player-loop times the game already accumulates (`Update`, `LateUpdate`, `FixedUpdate`, `Physics`, total CPU). |
| `RuntimeProfiler` public `TimeSpan`s | `runtime_profiler` | Values Facepunch writes every `ServerMgr.Update` (`ServerMgr_Update`, `Net_Cycle`, `Physics_SyncTransforms`, `Companion_Tick`, `BasePlayer_ServerCycle`). Accumulated over the second. No Azure analytics / `profile.*` convars required. |
| `HarmonyLoader.GetHarmonyMods()` + Harmony patch owners | `harmony_mods`, `harmony_mod_count` | Name, version, patch count per loaded mod. Extra Harmony IDs (not a loader entry) are reported as `version=extra`. |
| `HarmonyLoader.TryLoadMod` / `TryUnloadMod` | `harmony_mod_event` | `event=load` or `event=unload`. |
| Timed game methods | `server_update`, `work_queue`, `rpc_calls`, `invoke_execution`, `console_commands` | Same delayed Harmony timing patches as upstream (applied after the server is up). |
| Player connection | `connection_latency`, `client_performance` | Optional; toggle in config. |

## Setup

1. Grafana v9+ and InfluxDB **1.8** (v2 is not compatible with this line-protocol writer).
2. Set Influx `max-values-per-tag` and `max-series-per-database` to `0` (player series volume).
3. Create a database + user with write permission.
4. Stop the Rust server. Build and deploy:

```powershell
cd .cursor\HarmonyMods\HarmonyMetrics
.\build.ps1
```

That copies **only** `HarmonyMetrics.dll` into the server `HarmonyMods\` folder.

5. Start the server. Config is always `HarmonyConfig/HarmonyMetrics.json` next to the dedicated executable (resolved from `Application.dataPath`, not the process working directory). Edit it, then `harmonymetrics.reloadcfg`.
6. `harmonymetrics.reloadcfg`

```json
{
  "Enabled": true,
  "Influx Database Url": "http://127.0.0.1:8086",
  "Influx Database Name": "rust-server-metrics",
  "Influx Database User": "metrics",
  "Influx Database Password": "secret",
  "Server Tag": "staging",
  "Debug Logging": false,
  "Amount of metrics to submit in each request": 1000,
  "Gather Player Averages (Client FPS, Client Latency, Player FPS, Player Memory, Player Latency, Player Packet Loss)": true,
  "Gather Harmony Mod Inventory": true
}
```

`Server Tag` must be unique per dedicated server if several write to the same database.

## Commands

| Command | Who | What |
|---------|-----|------|
| `harmonymetrics.reloadcfg` | server admin | Reload `HarmonyConfig/HarmonyMetrics.json` |
| `harmonymetrics.status` | server admin | Ready flag, uploader buffer, Harmony mod count, NPC census breakdown |

## NPC census (vanilla vs mod)

When enabled (default), each performance tick writes:

**`players` fields:** `bots_vanilla`, `bots_mod`, `bots_grimm`, `bots_zombie`, `bots_grimm_other`, `bots_personalnpc`  
**`npc_census` fields:** same plus vanilla subtypes (`vanilla_tunnel`, `vanilla_scientist`, …) and `animals_total` / `animals_vanilla` / `animals_mod`

Classification (authoritative markers used by the spawn mods themselves):

| Bucket | Rule |
|--------|------|
| Grimm / NpcSpawn | Entity type name `CustomScientistNpc` (primary); `skinID == 11162132011012` secondary |
| ZombieHorde | Grimm + `ZombieNPC` behaviour |
| PersonalNPC | `BotOwnerComponent` |
| AnimalSpawn | `CustomAnimalNpc` type or `skinID == 11491311214163` |
| Vanilla bots | Everything else in `BasePlayer.bots` |

`bots` remains the full Facepunch total. Animal scan runs at most every 15s (full entity walk).

## Grafana

The upstream dashboard still works for the original measurements (`framerate`, `frametime`, `memory`, `network`, `rpc_calls`, …). Replace Oxide plugin panels with:

- measurement `harmony_mods`, tag `mod`, field `patches`
- measurement `harmony_mod_count`, fields `count`, `patches`
- measurement `cpu_sample`, fields `update_ms`, `total_cpu_ms`, …
- measurement `runtime_profiler`, fields `servermgr_update_ms`, `net_cycle_ms`, …

A starter dashboard JSON is in `res/Grafana-Dashboard.json` (upstream file with Oxide panels retargeted to Harmony mods).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/HarmonyMetrics.json` |
| Source | `.cursor/HarmonyMods/HarmonyMetrics/` |
| Runtime DLL | `HarmonyMods/HarmonyMetrics.dll` |
| Upstream reference | `.cursor/HarmonyMods/Rust-Server-Metrics-master/` (not loaded) |
