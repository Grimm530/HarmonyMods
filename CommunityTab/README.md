# CommunityTab

Forces Community / vanilla browser presentation by rewriting Steam `GameTags` at the same choke point FakePopulation uses (`SteamServer.GameTags`).

## Why the old strip failed

1. After `ServerTagCompressor`, modded is **`^z`** (oxide=`^o`, carbon=`^y`) — not the literal word `modded`.
2. Client **Mode** is not `server.tags` verbatim. `ServerInfo` picks Mode from `ModePriority` (in `Rust.Platform.Common`):

   `event → minigame → battlefield → builds → training → roleplay → creative → … → pvp → pve → vanilla`

   This server advertises `^r` (roleplay) and `^p` (pve from TruePVE’s `server.pve`). Those **outrank** `^v`, so Mode becomes **roleplay** / **pve**, never **vanilla** — even with a vanilla tag and no `^z`.

3. There is **no** assembly path that auto-adds `^z` when Harmony mods load. Live A2S on this host already had no modded marker; “not vanilla” was ModePriority.

## Config (`HarmonyConfig/CommunityTab.json`)

```json
{
  "StripModdedCategoryTags": true,
  "ForceVanillaMode": true
}
```

| Key | Default | Effect |
|-----|---------|--------|
| `StripModdedCategoryTags` | true | Remove `^z`/`^o`/`^y` (and legacy `modded`) so the server is not in the Modded tab |
| `ForceVanillaMode` | true | Remove mode tags above vanilla (including `^p` / `^r`) so client Mode = vanilla. Does **not** disable TruePVE / `server.pve` gameplay |

Tradeoff: with `ForceVanillaMode`, PvE/roleplay **filters** will not match this server (hostname can still say PvE).

## Patches

| Patch | Target | Purpose |
|-------|--------|---------|
| `SteamServer_GameTags` | `Steamworks.SteamServer.set_GameTags` Prefix | Rewrite every GameTags write before Steam (covers TruePVE’s `,pve` injection) |
| `ServerMgr_UpdateServerInformation` | Prefix | Clear `modded` / mode tags from `ConVar.Server._tags` before compress |

## Build / load

```
.\build.ps1
harmony.load CommunityTab
```

Expect log: `[CommunityTab] Rewrote SteamServer.GameTags ...`

Verify with A2S keywords: should keep `^v` / wipe / region, and should **not** contain `^z`, `^o`, `^p`, or `^r` when ForceVanillaMode is on.
