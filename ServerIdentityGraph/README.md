# ServerIdentityGraph (Harmony mod)

Writes **in-game team and clan** to `HarmonyData/ServerIdentityGraph` for Discord `/lookup`. BattleMetrics already has names, IPs, bans, and alts — this mod does not duplicate those.

## Identity

| Field | Value |
|-------|--------|
| **Name** | ServerIdentityGraph |
| **Entry** | `ServerIdentityGraph.ServerIdentityGraphHarmonyEntry` |
| **Config** | `HarmonyConfig/ServerIdentityGraph.json` |
| **Data** | `HarmonyData/ServerIdentityGraph/players/{steamId}.json` |

## What is recorded

| Field | Source |
|-------|--------|
| Current team: id, leader, members (Steam ID + name) | `RelationshipManager.PlayerTeam` |
| Current clan: id, **name**, members | `BasePlayer.serverClan` (`IClan.Name`) |

Captured ~1s after spawn (team) and again at ~8s (clan name is loaded asynchronously). Updated when someone is added to a team.

**Not recorded:** IPs, family-share owner, OS, kicks, Facepunch party `joinKey` (that is the *pre-spawn queue*, not the in-game team), Discord IDs (the dedicated server never receives them).

## File shape

```json
{
  "steamId": "7656119…",
  "name": "Grimm530",
  "lastSeen": "2026-08-23T18:32:49Z",
  "team": {
    "teamId": "3",
    "leader": { "steamId": "7656119…", "name": "Grimm530" },
    "members": [
      { "steamId": "7656119…", "name": "Grimm530" },
      { "steamId": "7656119…", "name": "Joshua 1:9" }
    ],
    "seenAt": "2026-08-23T18:32:50Z"
  },
  "clan": {
    "clanId": "1",
    "name": "Clan name",
    "members": [],
    "seenAt": "2026-08-23T18:32:50Z"
  }
}
```

## Build / deploy

```powershell
.\.cursor\HarmonyMods\ServerIdentityGraph\build.ps1
```

Then `harmony.load ServerIdentityGraph` (or restart). Discord `/lookup` reads the JSON off disk.
