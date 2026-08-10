# Leaderboard relay endpoint – same format as Oxide plugin

The Harmony mod POSTs batches to your `Relay.Url` in the **same shape as the Oxide UltimateLeaderboard plugin’s MySQL tables**. Your endpoint can upsert into the same database; players from Harmony servers will show on the website like everyone else, with 0 (or missing) for stats we don’t track.

## Request

- **Method:** `POST`
- **Content-Type:** `application/json`
- **Body:** one JSON object per batch.

## Body shape

```json
{
  "Updates": [
    {
      "UserId": 76561198000000000,
      "LootType": 6,
      "ShortName": "kills",
      "ItemValue": 5.0
    },
    {
      "UserId": 76561198000000000,
      "LootType": 5,
      "ShortName": "stones",
      "ItemValue": 1200.0
    }
  ],
  "Players": [
    {
      "UserId": 76561198000000000,
      "LastIP": "",
      "LastName": "PlayerName",
      "ConnectTime": "2025-02-24T12:00:00.0000000Z",
      "DisconnectTime": "2025-02-24T10:00:00.0000000Z",
      "TotalPlayTime": "12345.00",
      "Points": 10.5,
      "HiddenFromLeaderboard": 0
    }
  ]
}
```

- **Updates** – Rows for the **StatsStorage** table. `ItemValue` is the **current total** (not a delta). Same columns as the plugin: `UserId`, `LootType`, `ShortName`, `ItemValue`.
- **Players** – Rows for the **PlayerStats** table. Same columns as the plugin: `UserId`, `LastIP`, `LastName`, `ConnectTime`, `DisconnectTime`, `TotalPlayTime`, `Points`, `HiddenFromLeaderboard`.

Either `Updates` or `Players` (or both) may be present; either array may be empty.

## MySQL mapping (plugin‑compatible)

Use the same table/column names and types as the Oxide plugin.

### StatsStorage

- Table: your prefix + `StatsStorage`, e.g. `{prefix}StatsStorage`.
- For each item in `Updates`:

```sql
INSERT INTO StatsStorage (UserId, LootType, ShortName, ItemValue)
VALUES (@UserId, @LootType, @ShortName, @ItemValue)
ON DUPLICATE KEY UPDATE ItemValue = VALUES(ItemValue);
```

- **LootType** – Same enum as the plugin (e.g. 5 = Gather, 6 = Kill, 9 = Death, 1 = Construction, 16 = Upgrade, 10 = Craft, 11 = Crate, 12 = LootItems, 13 = Fishing, 14 = Puzzle, 18 = ExplosiveUsed, 19 = RecycleItem, etc.). Harmony only sends types it tracks; others will simply never appear for that player (website shows 0).

### PlayerStats

- Table: `{prefix}PlayerStats`.
- For each item in `Players`:

```sql
INSERT INTO PlayerStats (UserId, LastIP, LastName, ConnectTime, DisconnectTime, TotalPlayTime, Points, HiddenFromLeaderboard)
VALUES (@UserId, @LastIP, @LastName, @ConnectTime, @DisconnectTime, @TotalPlayTime, @Points, @HiddenFromLeaderboard)
ON DUPLICATE KEY UPDATE
  LastIP = VALUES(LastIP),
  LastName = VALUES(LastName),
  ConnectTime = VALUES(ConnectTime),
  DisconnectTime = VALUES(DisconnectTime),
  TotalPlayTime = VALUES(TotalPlayTime),
  Points = VALUES(Points),
  HiddenFromLeaderboard = VALUES(HiddenFromLeaderboard);
```

- **TotalPlayTime** – String with numeric format (e.g. `"12345.00"`), same as plugin.

## Result on the website

- Players from **Oxide** servers: full stats as today.
- Players from **Harmony** servers: same tables and columns; stats we track (kills, deaths, gather, build, upgrade, craft, recycle, loot, etc.) have real values; stats we don’t track (e.g. events, gambling, economy) are simply not in `StatsStorage` for them, so the site can treat missing (UserId, LootType, ShortName) as 0 and display them normally.

## Using the UltimateLeaderboard Discord bot as relay

The **UltimateLeaderboard Discord Bot** can act as the relay endpoint and write directly to your MySQL database (same one it uses for `/stats`).

1. **Enable the relay** in the bot’s `config.json`:
   ```json
   "relay": {
     "enabled": true,
     "port": 8765,
     "host": "0.0.0.0"
   }
   ```
2. **Start the bot** – it will listen for `POST /relay` on the given port (default 8765).
3. **Point the Harmony Leaderboard** at the bot: in `HarmonyConfig/Leaderboard.json` (next to your Rust server root) set:
   - `Relay.Enabled`: `true`
   - `Relay.Url`: `http://127.0.0.1:8765/relay` when LeaderBot runs on the **same machine** as the Rust server (recommended).  
     Use `http://<BOT_HOST>:8765/relay` only if the bot is on another host the server can reach. Do **not** use a public IP that does not forward port 8765.
   - `Relay.SyncAllOnLoad`: `true` (default) — on `harmony.load Leaderboard`, pushes all player JSON into MySQL so Discord `/stats` is not empty after a wipe or fresh DB.
4. **Firewall**: if the bot is remote, ensure port 8765 (or your chosen relay port) is open for inbound traffic from the game server to the bot host.

Then Discord `/stats` and in-game leaderboard will both read/write the same MySQL database.
