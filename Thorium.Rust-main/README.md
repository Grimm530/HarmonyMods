# Thorium Rust Mod

Harmony mod for Rust dedicated servers that captures anti-cheat telemetry (player snapshots, RPCs, damage, entity events) and streams it to the Thorium backend over WebSocket. No Oxide dependency.

---

## 1. Mod Identity

| Item | Description |
|------|-------------|
| **Name** | Thorium (assembly: `Thorium`) |
| **Purpose** | Anti-cheat telemetry: capture in-game events and player state, serialize as Protobuf, send to `gateway.thorium.ac` |
| **Primary responsibilities** | Patch capture (player tick, RPC, hurt, die, disconnect, move item, entity kill, analytics), config (YAML), console commands, WebSocket client, snapshot batching and flush |
| **Key flags** | `ThoriumConfigService.HasValidToken`, `ThoriumLoader.__serverStarted`, `ThoriumConfigService.DebugMode`, `ThoriumClientService.IsConnected`, `AntiCheatSnapshotProcessor.IsWorkerRunning` |

---

## 2. Project Structure & Topology

| Path / component | Purpose |
|------------------|---------|
| **Assembly** | `Thorium.Rust` → `Thorium.dll`, `netstandard2.1`, references from `../../deps/$(GamePlatform)` (Linux/Windows) |
| **Entry** | `ThoriumLoader` implements `IHarmonyModHooks`; `OnLoaded` / `OnUnloaded` drive lifecycle |
| **Config** | `../../.thorium/thorium.yml` (relative to server root); YAML with `ServerToken`, `Debug` |
| **State flow** | Config load at init → `ThoriumConfigService`; patches enqueue snapshots / write to `DataHandler` caches → `AntiCheatSnapshotProcessor` flushes per player → `ThoriumClientService` sends binary/JSON to backend |

**Folders:**

- `src/Thorium.Rust/` — main code: `ThoriumLoader`, `ConsoleCommands`, `AntiCheatSnapshotProcessor`, config, core, patches, models, services
- `scripts/` — `SteamDownloader.ps1` (Steam DepotDownloader, fetch Rust Managed DLLs), `unprivate-dependencies.ps1` (publicize Apex/Assembly-CSharp/Facepunch/Rust/NewAssembly, copy rest to `deps`)
- Root `.bat` files — call scripts to refresh `raw-deps` and `deps` for Windows (public/staging) or Linux

---

## 3. Persistent Data Model

- **No per-player file persistence.** All state is in-memory: snapshot buffers, WebSocket queue, DataHandler caches.
- **Config** (`thorium.yml`): `ServerToken`, `Debug`. Stored under server root `../../.thorium/thorium.yml`. Loaded at init; `SetServerToken` / `SetDebugMode` write back.
- **Snapshot buffers**: `AntiCheatSnapshotProcessor` holds per-SteamId queues of `PlayerSnapshot` (capped per player, and total pool capacity). Flushed every 1s and sent as `ThoriumBatch` (Protobuf).
- **DataHandler caches**: `PacketCache`, `PvpCache`, `JoinCache`, `DamageCache`, `EntityCache` (MemoryStreams). Drained and serialized into the batch on flush; not persisted to disk.

---

## 4. Configuration Schema

| Field | Type | Default | Behavioral impact |
|-------|------|--------|-------------------|
| `ServerToken` / `server_token` | string | (empty) | Required for backend auth; no token → no connect, snapshots dropped |
| `Debug` | bool | false | When true, extra `Debug.Log` and config logging |

Config is YAML (simple key: value); keys case-insensitive. Values in double/single quotes are trimmed. Config path: `ThoriumConfigService` uses `Server.rootFolder` + `../../.thorium/thorium.yml`.

---

## 5. Console Commands

All under prefix `thorium`, registered on `ConsoleSystem.Index.Server.Dict`. Server console only for setup/debug.

| Command | Permission | Purpose | Side effects |
|---------|------------|---------|--------------|
| `thorium.status` | ServerUser, ServerAdmin, ClientAdmin | Print token status, connected, debug, worker running, buffer count | None |
| `thorium.setup <token>` | ServerAdmin only | Set server token and save config; trigger connect coroutine | Writes `thorium.yml`, starts connect; if no connect, reconnect loop runs |
| `thorium.debug [true\|false]` | ServerAdmin only | Get or set debug mode | Writes config when setting |

---

## 6. Harmony Patches & Event Flow

Patches only run when `DataHandler.IsConfigured` (valid token) where applicable. Snapshot types use `SnapshotTypeEnums` (e.g. PlayerTick, Join, Leave, Hurt, Die, MoveItem, EntityKill, stash events).

| Target | Patch type | Purpose |
|--------|------------|---------|
| **ServerMgr.OpenConnection** | Postfix | Call `ThoriumLoader.OnServerStarted()` so backend connect and services start after server is up |
| **ServerMgr.OnPlayerTick** | Prefix | Build `PlayerSnapshot` from tick + player state (position, velocity, input, eyes, model state, etc.), enqueue to `AntiCheatSnapshotProcessor` |
| **ServerMgr.OnRPCMessage** | Prefix | On `Message.Type.RPCMessage`, write RPC id, user, entity id, time, frame, position, raw payload length + bytes into `DataHandler.PacketCache`; optionally invoke `ThoriumLoader.rpcActions` by rpcId |
| **BasePlayer.PlayerInit** | Postfix | Enqueue Join snapshot; cleanup old buffer for that SteamId |
| **BasePlayer.OnDisconnected** | Postfix | Enqueue Leave snapshot; `AntiCheatSnapshotProcessor.CleanupPlayer(steamId)` |
| **BasePlayer.Hurt** | Postfix | Enqueue Hurt or HurtEnv snapshot from HitInfo |
| **BasePlayer.Die** | Postfix | Enqueue Die snapshot |
| **PlayerInventory.MoveItem** | Postfix | Enqueue MoveItem snapshot (item move within inventory) |
| **BaseNetworkable.Kill** | Postfix | Enqueue EntityKill snapshot when killer is player |
| **Analytics.Azure.OnEntityBuilt** | Postfix | Stash-built-over / entity-built analytics capture |
| **Analytics.Azure.OnEntityDestroyed** | Postfix | Entity-destroyed analytics capture |
| **BasePlayer.OnReceiveTick** | (present but deprecated) | Replaced by ServerMgr.OnPlayerTick for richer tick data |

Event flow: **Patches** → enqueue `PlayerSnapshot` or write to **DataHandler** caches → **AntiCheatSnapshotProcessor** worker (1s interval) flushes per-player queues and drained caches → **ThoriumBatchProtobufSerializer** → **ThoriumClientService.SendBinaryOrQueueAsync** → WebSocket or pending queue.

---

## 7. Lifecycle & State Machine

- **OnLoaded**: Register unhandled exception handler, init on main thread (scheduler + config), start `StartWhenServerReadyRoutine` (wait for `ServerMgr.Instance`, then call `OnServerStarted`).
- **OnServerStarted** (once): Set server info, `ConnectToBackendRoutine` (auth → map upload → WebSocket connect), start `AntiCheatSnapshotProcessor` worker, register console commands.
- **OnUnloaded**: Stop worker, disconnect client, reset services (`ThoriumClientService`, `AntiCheatSnapshotProcessor`, `ConsoleCommands`, `DataHandler`, config), destroy `ThoriumUnityScheduler`.
- **Ordering**: Config before patches matter; connection after server info; worker started after connect attempt; cleanup order: worker → client → reset → scheduler.

---

## 8. Build & Dependencies

- **Configurations**: `Linux`, `Windows` (output under `bin/Linux/`, `bin/Windows/`). References from `..\..\deps\$(GamePlatform)`.
- **Dependencies**: Rust game assemblies (Assembly-CSharp, Facepunch.*, Rust.*, etc.) must be present in `deps/<platform>`. They are **not** copied to output (`ClearReferenceCopyLocalPaths`).
- **Getting DLLs**: Run from repo root:
  - **Windows public**: `update-win-dependencies.bat` (SteamDownloader for app 258550, then unprivate-dependencies into `deps/windows/`)
  - **Windows staging**: `update-win-staging.bat` (same with staging branch)
  - **Linux**: use `update-lin-dependencies.bat` / `update-lin-staging.bat` with `-platform linux`
- **Scripts**: `SteamDownloader.ps1` needs PowerShell 6+; downloads DepotDownloader, fetches `RustDedicated_Data/Managed/*.dll` to `raw-deps/<platform>`. `unprivate-dependencies.ps1` publicizes selected assemblies and copies others to `deps/<platform>`.

---

## 9. What NOT to Touch Without Care

- **Patch targets**: Method names/signatures depend on Rust build; Harmony target types must match (e.g. `ServerMgr.OnRPCMessage`, `BasePlayer.Hurt(HitInfo)`).
- **Config path and format**: `../../.thorium/thorium.yml` and the simple YAML parser are assumed; changing path or format can break setup/status.
- **Entity lookup**: Use `BaseNetworkable.serverEntities.Find(NetworkableId)` when you have an ID; do not iterate `serverEntities` for lookup. Do not retry with expensive search if `Find` fails.
- **Unity main thread**: WebSocket and game APIs are used from Unity context; coroutines are run via `ThoriumUnityScheduler` (main thread). Do not block main thread with sync I/O.
- **Snapshot pooling**: `PlayerSnapshot` and `AntiCheatSnapshot` use object pools; `ResetState` and pool return rules must stay consistent (e.g. do not pool `EventSnapshot`).
- **Serialization**: Protobuf layout and `ThoriumBatch` / `DataHandlerPayload` are backend-contract; changing wire format can break the service.

---

## 10. Performance Anti-Patterns

- **Do not iterate `BaseNetworkable.serverEntities`** for lookup — use `Find(NetworkableId)` when you have an ID.
- **Do not retry with expensive search** if `Find()` fails — set ID to 0 and skip.
- **Do not use reflection** for methods in this mod — use internal/public and call directly.
- **Do not invalidate valid cached data** in loops — skip already-cached entities.
- **DataHandler cache size**: `PacketCache` (and others) are capped by `DataHandler.MaxCacheSize`; overflow is skipped to avoid unbounded memory.
- **Snapshot caps**: Per-player queue and pool capacities limit memory; changing them affects high-player and high-tick scenarios.

---

## 11. Backend & Wire Format

- **Endpoint**: `gateway.thorium.ac` (HTTPS auth, WSS for anticheat).
- **Auth**: GET `/api/session/auth` with `X-SERVER-TOKEN`; response session token used for WSS `X-SESSION-TOKEN`.
- **Map**: Level URL or map file upload sets `MapHash`; sent with server info after connect.
- **Traffic**: Server info as JSON; snapshot batches as binary Protobuf via `ThoriumBatchProtobufSerializer`. Pending messages queued when disconnected; flushed after reconnect.

---

## 12. Related Docs

- **READING_WEAPONFIRED_RPC.md** — How RPC (e.g. weapon fired) reading and `rpcActions` work for this mod.

---

## License

See [LICENSE](LICENSE) in the repository.
