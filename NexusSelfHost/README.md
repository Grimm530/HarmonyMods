# NexusSelfHost

Harmony mod that makes the Rust dedicated server (and game client) work with a **self-hosted Nexus API** instead of Facepunch’s. Patches auth (Bearer token forwarding), endpoint (NEXUS_ENDPOINT from env), and TLS (accept self-signed cert for localhost and server IP). The mod now writes `HarmonyConfig/NexusSelfHost.json` on first load; environment variables still override config values when set.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Allow Rust server + client to use a self-hosted Nexus-compatible API (HTTPS, self-signed cert, zone secret auth) |
| **Entry point** | `NexusSelfHost` implements `IHarmonyModHooks` |
| **Config / data / images** | Config: `HarmonyConfig/NexusSelfHost.json` (created automatically on first load). Environment variables still override config values when set. |
| **Loading** | Automatic from `HarmonyMods/` at game startup; or `harmony.load NexusSelfHost` (not `o.load`) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `NexusSelfHost.cs` | Lifecycle; loads/writes `HarmonyConfig/NexusSelfHost.json`; logs patches |
| `NexusSelfHostConfig.cs` | Config schema + load/write logic for `HarmonyConfig/NexusSelfHost.json` |
| `NexusSelfHostOptions.cs` | Effective option resolver (config defaults + environment variable override precedence) |
| `PortalTransferRouting.cs` | Resolves destination arrival points from `HarmonyConfig/NexusStaticPortals.json` by matching incoming source zone to a portal target on the destination server |
| `OutpostTransferRouting.cs` | Locates destination Outpost / compound and computes monument-relative landing points for console transfers |
| `NexusSelfHost.csproj` | net48; references Rust.Harmony, 0Harmony, Facepunch.Nexus, System.Net.Http, UnityEngine.CoreModule, System |
| `build.ps1` | `dotnet build -c Release`; copies `bin/Release/net48/NexusSelfHost.dll` to server-root `HarmonyMods/` |
| `Patches/BaseNexusClient_DispatchError_SuppressSocketNotConnected_Patch.cs` | Suppress repeated "Socket is not connected for zone" log spam when WSS fails |
| `Patches/NexusConnector_SendRequestImpl_Patch.cs` | Forward HttpClient default Bearer when authToken is null; **normalize** secret (strip `, Bearer …` duplication) |
| `Patches/HttpClient_SendAsync_DedupeAuthorization_Patch.cs` | Prevents merged duplicate `Authorization` on the wire (default + per-request Bearer) |
| `Patches/NexusConnector_Ctor_HttpTimeout_Patch.cs` | Raises NexusConnector `HttpClient.Timeout` to tolerate slow `/zone/message` and zone-status traffic under load |
| `Patches/Debug_Log_SuppressNexusRpcUnexpectedResponse_Patch.cs` | Filters benign “unexpected nexus RPC response (timed out)” `Debug.Log` spam |
| `Patches/NexusServer_Initialize_EndpointFromEnv_Patch.cs` | Set nexus.endpoint from NEXUS_ENDPOINT when still Facepunch default |
| `Patches/NexusServerLogger_Log_SuppressZoneSocketTlsSpam_Patch.cs` | Suppress "Lost connection to Nexus zone socket" + TLS exception log spam |
| `Patches/NexusSocketConnector_Connect_AcceptCert_Patch.cs` | Set ServicePointManager.ServerCertificateValidationCallback before WSS Connect |
| `Patches/UnityTlsContext_VerifyCallback_AcceptLocalhost_Patch.cs` | Accept self-signed cert in UnityTls for localhost and any IPv4 (server public IP) |
| `Patches/BasePlayer_OnDisconnected_NotifyApi_Patch.cs` | After disconnect, fire-and-forget HTTPS `POST …/zone/player/disconnect?playerId=` with `nexus.secretKey` Bearer (self-hosted API only) |
| `Patches/ServerMgr_Initialize_DeferredEnterGamePatch.cs` | After `ServerMgr.Initialize`, applies deferred `BasePlayer` patches (`PlayerInit` + optional `EnterGame` log) so early `PatchAll` does not trigger `FileStorage` (Sqlite error 14; see guide) |
| `Patches/BasePlayer_PlayerInit_InvalidatePersistCachePatch.cs` | Prefix: clear `cachedPersistantPlayer` when Nexus started. Postfix: reload `PersistantPlayerInfo` from Nexus and call `SendAsSnapshot` again so the client gets correct `persistantData` (blueprints) after connect / server restart |
| `Patches/BasePlayer_EnterGame_NexusBlueprintLog_Patch.cs` | Postfix body only: after `EnterGame` (console `has spawned`), log Nexus `blueprints.12` on the in-memory `NexusPlayer` for non-bot players when `NexusServer.Started` |
| `Patches/NexusServer_TransferEntity_PortalsOnly_Patch.cs` | Prefix on `NexusServer.TransferEntity`: block `ocean` and `ferry` unless `NEXUS_PORTALS_ONLY_TRANSFER=0` (`console` unchanged) |
| `Patches/TransferHandler_RepositionEntitiesEmptyGuard_Patch.cs` | Prefix on `TransferHandler.RepositionEntitiesFromTransfer`: skip when `entities` is empty (avoids vanilla `ArgumentOutOfRangeException`); opt out `NEXUS_GUARD_EMPTY_TRANSFER=0` |
| `Patches/TransferHandler_RepositionEntitiesConsoleSafeSpawn_Patch.cs` | Prefix (captures source root) + Postfix on `RepositionEntitiesFromTransfer`: logs transfer-fix diagnostics when enabled, re-picks `ServerMgr.FindSpawnPoint` if still too near source X/Z, then lifts the bundle above terrain using `TransformUtil.GetGroundInfo` / `TerrainMeta.HeightMap`. Opt out `NEXUS_TRANSFER_SKIP_SAFE_SPAWN=1`. |

## Config file

On first server load, the mod writes:

`HarmonyConfig/NexusSelfHost.json`

Use that file for persistent defaults. If an environment variable is also set, the **environment variable wins**.

Example:

```json
{
  "Debug": {
    "VerboseHttp": false,
    "LogBlueprintOnConnect": true,
    "LogPlayerInitResync": false,
    "LogTransferFix": true,
    "VerboseTransferFix": false,
    "LogTransferEntry": true
  },
  "Nexus": {
    "NotifyPlayerDisconnect": true
  },
  "Transfer": {
    "SkipSafeSpawn": false,
    "ConsoleMinSepMeters": 12.0,
    "ConsoleRespawnTries": 48,
    "GroundMarginMeters": 2.0
  },
  "PortalTransfer": {
    "Enabled": true,
    "ForceConsoleTransfers": true,
    "UsePortalRotation": true,
    "ForwardOffsetMeters": 2.25,
    "UpOffsetMeters": 0.1
  },
  "OutpostTransfer": {
    "Enabled": false,
    "ForceConsoleTransfers": true,
    "UseMonumentRotation": true,
    "DefaultHeightAboveRoot": 2.0,
    "LocalOffset": {
      "X": 0.0,
      "Y": 0.0,
      "Z": 0.0
    },
    "ReferenceZoneKey": "",
    "ReferencePointEnabled": false,
    "ReferenceWorldPosition": {
      "X": 0.0,
      "Y": 0.0,
      "Z": 0.0
    }
  },
  "Patches": {
    "GuardEmptyTransfer": true,
    "BlockOceanFerryTransfers": true,
    "SkipPersistCacheInvalidate": false,
    "SkipBlueprintSnapshotResync": false
  }
}
```

## Environment Variables (server)

| Variable | Purpose |
|----------|---------|
| **NEXUS_ENDPOINT** | Full URL of the self-hosted API (e.g. `https://70.8.154.251:5001/`). Use the **server’s public IP** so the game client can reach the API; if you use `127.0.0.1`, the client will try its own PC and get connection refused. |
| **NEXUS_SECRET_KEY** | Zone secret (e.g. `svr1-secret`). Must match one of the zone `SecretKey` values in the Nexus API `appsettings.json`. |
| **NEXUS_DEBUG** | Optional. Set to `1` for verbose console logs (endpoint before/after, SendRequestImpl authToken). When set, this overrides `Debug.VerboseHttp` from `HarmonyConfig/NexusSelfHost.json`. |
| **NEXUS_NOTIFY_PLAYER_DISCONNECT** | Optional. Set to `0` to **disable** the `BasePlayer.OnDisconnected` → `POST /zone/player/disconnect` notify. Overrides `Nexus.NotifyPlayerDisconnect`. |
| **NEXUS_LOG_BLUEPRINT_CONNECT** | Optional. Set to `0` to **disable** console lines after each real player `EnterGame` (right after `has spawned`) showing whether `blueprints.12` was present on the in-memory `NexusPlayer` (Binary blob size). Overrides `Debug.LogBlueprintOnConnect`. |
| **NEXUS_LOG_TRANSFER_ENTRY** | Optional. Set to `0` to disable the early transfer-entry log (`method` / `from` / `to` / `entities`) emitted before reposition handling. Overrides `Debug.LogTransferEntry`. |
| **NEXUS_LOG_TRANSFER_FIX** | Optional. Set to `0` to disable transfer-fix diagnostics for `console` transfers. Overrides `Debug.LogTransferFix`. |
| **NEXUS_VERBOSE_TRANSFER_FIX** | Optional. Set to `1` for more transfer-fix detail (skip reasons, chosen spawn candidate, no-lift-needed messages). Overrides `Debug.VerboseTransferFix`. |
| **NEXUS_SKIP_PERSIST_CACHE_INVALIDATE** | Optional. Set to `1` to **disable** the `PlayerInit` prefix that clears `InvalidateCachedPeristantPlayer` when Nexus is started (not recommended). Overrides `Patches.SkipPersistCacheInvalidate`. |
| **NEXUS_SKIP_BLUEPRINT_SNAPSHOT_RESYNC** | Optional. Set to `1` to **disable** the `PlayerInit` postfix that forces a second `SendAsSnapshot` after reloading persist data from Nexus (for debugging only). Overrides `Patches.SkipBlueprintSnapshotResync`. |
| **NEXUS_PORTALS_ONLY_TRANSFER** | Optional. Set to `0` to **allow** **ocean** (Nexus island triggers) and **ferry** transfers again. Overrides `Patches.BlockOceanFerryTransfers`. |
| **NEXUS_GUARD_EMPTY_TRANSFER** | Optional. Set to `0` to **disable** the guard on `TransferHandler.RepositionEntitiesFromTransfer` (vanilla behavior: empty `entities` throws). Overrides `Patches.GuardEmptyTransfer`. |
| **NEXUS_TRANSFER_SKIP_SAFE_SPAWN** | Optional. Set to `1` to **disable** the console-transfer safe-spawn postfix (vanilla reposition only). Overrides `Transfer.SkipSafeSpawn`. |
| **NEXUS_TRANSFER_CONSOLE_MIN_SEP** | Optional. Horizontal meters between post-reposition root and **source** root below which a new spawn is forced (default `12`). Overrides `Transfer.ConsoleMinSepMeters`. |
| **NEXUS_TRANSFER_CONSOLE_RESPAWN_TRIES** | Optional. Max `FindSpawnPoint(null, 0)` attempts when searching for a farther spawn (default `48`). Overrides `Transfer.ConsoleRespawnTries`. |
| **NEXUS_TRANSFER_GROUND_MARGIN** | Optional. Meters above detected ground the transfer root is placed after vertical snap (default `2`). Uses `TransformUtil.GetGroundInfo` from high altitude, then `TerrainMeta.HeightMap.GetHeight` as fallback. Overrides `Transfer.GroundMarginMeters`. |
| **NEXUS_TRANSFER_PORTAL_ENABLED** | Optional. Set to `0` to disable destination arrival routing through `HarmonyConfig/NexusStaticPortals.json`. Overrides `PortalTransfer.Enabled`. |
| **NEXUS_TRANSFER_PORTAL_FORCE_CONSOLE** | Optional. Set to `0` to stop forcing `console` transfers to the matched portal arrival point while leaving the section available for testing. Overrides `PortalTransfer.ForceConsoleTransfers`. |
| **NEXUS_TRANSFER_PORTAL_USE_ROTATION** | Optional. Set to `0` to apply the arrival offset without rotating it by the matched portal door rotation. Overrides `PortalTransfer.UsePortalRotation`. |
| **NEXUS_TRANSFER_PORTAL_FORWARD_OFFSET** | Optional. Forward offset from the matched portal door transform for the final arrival point (default `2.25`). Negative values land behind the door. Overrides `PortalTransfer.ForwardOffsetMeters`. |
| **NEXUS_TRANSFER_PORTAL_UP_OFFSET** | Optional. Small vertical offset added before the terrain snap safety net (default `0.1`). Overrides `PortalTransfer.UpOffsetMeters`. |
| **NEXUS_TRANSFER_OUTPOST_ENABLED** | Optional. Set to `0` to disable Outpost routing and keep only the old safe-spawn / terrain correction behavior. Overrides `OutpostTransfer.Enabled`. |
| **NEXUS_TRANSFER_OUTPOST_FORCE_CONSOLE** | Optional. Set to `0` to disable forced Outpost routing for `console` transfers without turning off the whole section. Overrides `OutpostTransfer.ForceConsoleTransfers`. |
| **NEXUS_TRANSFER_OUTPOST_USE_ROTATION** | Optional. Set to `0` to apply `OutpostTransfer.LocalOffset` as world-space delta instead of rotating it with the detected Outpost monument. Overrides `OutpostTransfer.UseMonumentRotation`. |
| **NEXUS_TRANSFER_OUTPOST_DEFAULT_HEIGHT** | Optional. Height used above the Outpost root when `LocalOffset` is still zeroed (default `2`). Overrides `OutpostTransfer.DefaultHeightAboveRoot`. |

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| **BaseNexusClient_DispatchError_SuppressSocketNotConnected_Patch** | `Facepunch.Nexus.BaseNexusClient.DispatchError(Exception)` | Prefix | When the message is "Socket is not connected for zone ...", skip the original so the log is not spammed every ~30s; HTTP API still works. |
| **NexusConnector_SendRequestImpl_Patch** | `Facepunch.Nexus.Connector.NexusConnector.SendRequestImpl<T>` (closed for ZoneDetails, ZonePlayerDetails, ZonePlayerLogin, RegisterTransfersResponse, CompleteTransfersResponse, Int32) | Prefix | If `authToken` is null, set it from the connector’s `HttpClient.DefaultRequestHeaders.Authorization.Parameter` so requests (e.g. GET /zone/info) send the Bearer token and the self-hosted API returns 200. **Normalizes** the secret (first segment before comma, strip stray `Bearer ` prefix). Does **not** write `DefaultRequestHeaders` (see **HttpClient_SendAsync_DedupeAuthorization_Patch**). |
| **HttpClient_SendAsync_DedupeAuthorization_Patch** | `System.Net.Http.HttpClient.SendAsync(HttpRequestMessage, CancellationToken)` | Prefix + Postfix | When the request already has `Authorization`, temporarily clears `DefaultRequestHeaders.Authorization` so HttpClient does not merge two Bearer values into one comma-separated header on the wire. Restores defaults after `SendAsync` returns (fixes Nexus API `DebugAuth` duplicate-header noise). |
| **Debug_Log_SuppressNexusRpcUnexpectedResponse_*_Patch** | `UnityEngine.Debug.Log(object)` and `Log(object, Object)` | Prefix | Suppresses the benign vanilla line about an unexpected Nexus RPC response timing out (common under load). One-time `Console.WriteLine` explains; **NexusConnector_Ctor_HttpTimeout_Patch** already raises HTTP timeout. |
| **NexusServer_Initialize_EndpointFromEnv_Patch** | `NexusServer.Initialize` | Prefix | Read `ConVar.Nexus.endpoint`; if it’s empty or still the Facepunch default URL, set it from `NEXUS_ENDPOINT` (with `http://` prepended if missing). Ensures the self-hosted URL is used before the game makes Nexus calls. |
| **NexusServerLogger_Log_SuppressZoneSocketTlsSpam_Patch** | `NexusServerLogger.Log(NexusLogLevel, string, Exception)` | Prefix | Suppress "Lost connection to Nexus zone socket" when the exception chain contains UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED or "Handshake failed"; suppress repeated "Connecting to nexus socket...". Reduces disk I/O from 24/7 log spam. |
| **NexusSocketConnector_Connect_AcceptCert_Patch** | `Facepunch.Nexus.Connector.NexusSocketConnector.Connect` | Prefix | Before Connect, set `ServicePointManager.ServerCertificateValidationCallback` to accept any cert so WSS to a self-signed HTTPS endpoint can succeed (if the stack respects this callback). |
| **UnityTlsContext_VerifyCallback_AcceptLocalhost_Patch** | `Mono.Unity.UnityTlsContext.VerifyCallback` (instance, 2 params) | Postfix | When the result has NOT_TRUSTED and the connection host is 127.0.0.1, localhost, or any IPv4 (e.g. server public IP), override result to success so HTTPS/WSS to the self-hosted API does not fail on the self-signed cert. |
| **BasePlayer_OnDisconnected_NotifyApi_Patch** | `BasePlayer.OnDisconnected` | Postfix | Reads `ConVar.Nexus.endpoint` + `secretKey`, skips if URL contains `facepunch.com`, skips if `BaseEntity.IsTransferring()` (Nexus warp in progress - `RegisterTransfers` already updated home). Then `Task.Run` + `HttpClient.PostAsync` to `{endpoint}zone/player/disconnect?playerId=`. If you also use **NexusHomeDisconnect** Oxide, pick one (duplicate POSTs are redundant). |
| **ServerMgr_Initialize_DeferredEnterGamePatch** | `ServerMgr.Initialize` | Postfix | Once: applies deferred `BasePlayer.PlayerInit` prefix/postfix and optional `EnterGame` postfix. Deferred so patching `BasePlayer` does not run during `BeforeSceneLoad` (avoids `FileStorage` / `sv.files.*.db` error 14). |
| **BasePlayer_PlayerInit_InvalidatePersistCachePatch** | `BasePlayer.PlayerInit` | Prefix + postfix (via deferred patch) | Prefix: `InvalidateCachedPeristantPlayer` when Nexus started. Postfix: reload `PersistantPlayerInfo` from Nexus and second `SendAsSnapshot` for client blueprint sync. Opt out: `NEXUS_SKIP_PERSIST_CACHE_INVALIDATE=1` / `NEXUS_SKIP_BLUEPRINT_SNAPSHOT_RESYNC=1`. |
| **BasePlayer_EnterGame_NexusBlueprintLog_Patch** | `BasePlayer.EnterGame()` | Postfix (applied by deferred patch) | If `NexusServer.Started` and player is not a bot (`IsBot`), resolves `NexusServer.TryGetPlayer` + `TryGetVariable("blueprints.12")` and logs `OK Binary protoBytes=N` or a **warning** if lookup fails. Runs at **end** of `EnterGame` (a few lines after console `has spawned`). |
| **NexusServer_TransferEntity_PortalsOnly_Patch** | `NexusServer.TransferEntity(BaseEntity,string,string,bool)` | Prefix | Blocks **ocean** and **ferry** transfers by default so map-edge islands and ferries do not move players; **console** (Oxide Portals + admin `nexus.transfer`) is unchanged. Opt out: `NEXUS_PORTALS_ONLY_TRANSFER=0`. |
| **TransferHandler_RepositionEntitiesEmptyGuard_Patch** | `Rust.Nexus.Handlers.TransferHandler.RepositionEntitiesFromTransfer()` | Prefix | If `Request.entities` is null or count 0, skip original (no `[0]` access). Reduces log spam when Nexus delivers an empty transfer payload (e.g. kick mid-handshake). Opt out: `NEXUS_GUARD_EMPTY_TRANSFER=0`. |
| **TransferHandler_RepositionEntitiesConsoleSafeSpawn_Patch** | `Rust.Nexus.Handlers.TransferHandler.RepositionEntitiesFromTransfer()` | Prefix + Postfix | Prefix logs `method` / `from` / `to` / `entities`, then stores source root from proto before vanilla. Postfix: if `Request.transfer.method` is `console`, it first tries to route the bundle to the portal anchor on the destination server whose `NexusTransferTargetZoneKey` matches the incoming source zone in `HarmonyConfig/NexusStaticPortals.json`; if that cannot be resolved it can fall back to Outpost routing and then the older procedural respawn separation logic. It then **always** lifts the bundle if the root is below ground at its final X/Z (`TransformUtil.GetGroundInfo` ray, else `TerrainMeta.HeightMap.GetHeight`, margin `NEXUS_TRANSFER_GROUND_MARGIN`). Prevents map-coordinate carryover from dropping players under terrain and lets each server own its arrival points through its local portal config. |

## Blocking ocean / ferry transfers (Grimmzone)

Legacy **`Portals.cs`** uses `NexusServer.TransferEntity(..., "console", false)` like vanilla. This mod **only blocks** **`ocean`** (Nexus island triggers) and **`ferry`**. Admin **`nexus.transfer`** also uses **console** and remains available.

**NexusApi** does not need changes; this is enforced on the game server.

## Lifecycle

- **OnLoaded:** Log that NexusSelfHost loaded (SendRequestImpl Bearer + optional player disconnect notify + optional blueprint connect log).
- **OnUnloaded:** No cleanup (patches are removed by Harmony on unload).

## Dependencies

- **Rust server:** Must have `NEXUS_ENDPOINT` and `NEXUS_SECRET_KEY` set (e.g. in the batch that starts Rust) so the endpoint patch and auth patch are useful.
- **Nexus API:** The self-hosted API (for this repo, typically `nexusPVESERVER/NexusApi`) must be running and must allow the zone secret. In practice the mod expects the API shape documented there: `GET /1`, `GET /player/info`, `GET /zone/info`, `POST /zone/message`, `GET/POST /zone/socket`, and `POST /zone/player/disconnect`. See `nexusPVESERVER/NexusApi/README.md` and `nexusPVESERVER/README.md`.

## Troubleshooting

- **Nexus API logs show `Bearer svr1-secret, Bearer svr1-secret` (duplicate header)** — Caused by HttpClient merging **DefaultRequestHeaders.Authorization** (set in `NexusZoneConnector` ctor) with **per-request** `Authorization` in `SendRequestImpl`. Fixed by **`HttpClient_SendAsync_DedupeAuthorization_Patch`** (clears default for the duration of `SendAsync`) plus secret normalization in **`NexusConnector_SendRequestImpl_Patch`**. Rebuild with `build.ps1`, copy `NexusSelfHost.dll` to each server’s `HarmonyMods/`, restart Rust.

## What NOT to Touch

- Do not patch Oxide or call Oxide APIs; this mod is standalone and only patches Nexus/Unity/game code.
- Do not use FileStorage or other game storage in OnLoaded (see HARMONY_MODS_GUIDE.md). Do not patch `BasePlayer.EnterGame` during automatic `PatchAll` at startup for the same reason—defer that patch until after `ServerMgr.Initialize` (this mod uses `ServerMgr_Initialize_DeferredEnterGamePatch`).

## Build and Deploy

References come from the **Rust server install** (`RustDedicated_Data\Managed`), not from git. `build.ps1` searches, in order: `$env:RUST_MANAGED`, then `..\..\RustDedicated_Data\Managed` (parent of `.cursor` when the mod lives under `<repo>\.cursor\NexusSelfHost`), then `C:\svr1\...`, `D:\!RustServer\...`, `D:\!Grimmzone\...`. Set explicitly if needed:

`$env:RUST_MANAGED = 'C:\svr1\RustDedicated_Data\Managed'`

Or: `dotnet build -c Release -p:RustManagedPath="C:\svr1\RustDedicated_Data\Managed"`

1. From the mod folder:  
   `powershell -ExecutionPolicy Bypass -File build.ps1`
2. The script builds Release and copies `NexusSelfHost.dll` to the repo root `HarmonyMods\` (copy from there to each server’s `HarmonyMods\` if installs live elsewhere).
3. Restart the Rust server (or run `harmony.load NexusSelfHost` if the loader supports late load) so the mod is applied.

Reference: `.cursor/!AssemblyFiles/Harmony-Assembly/HARMONY_MODS_GUIDE.md` (Harmony mod directory layout, loading, creating new mods).
