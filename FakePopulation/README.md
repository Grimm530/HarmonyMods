# FakePopulation Harmony Mod

Shows an **inflated player count** in the Steam server browser. Use cases:
- Servers with AI/NPC bots that reserve slots (e.g. limited to 30 AI) so real players (45–65+) can still join a "100 player" server
- Making low-pop servers appear more active to attract players

**Note:** Only affects the **displayed** count in the server browser. Does not change actual slots, connection logic, or queue.

---

## Installation

1. Build: Run `build.ps1` or `dotnet build FakePopulation\FakePopulation.csproj -c Release`
2. Copy `FakePopulation.dll` to `HarmonyMods/` (or use the build script which does this)
3. Config is created at `HarmonyConfig/FakePopulation.json` on first load

---

## Configuration

**HarmonyConfig/FakePopulation.json:**
```json
{
  "BonusPlayers": 30
}
```

- **BonusPlayers** (0–999): Extra players to add to the displayed count
  - `0` = disabled (shows real count)
  - `30` = e.g. 20 real players → shows 50
  - Keep it reasonable (e.g. 20–40 for AI/NPC slots) so players can still join

---

## server.cfg / Server Host Notes

If your host or setup uses tags:
- Add `server.population` if supported
- Use tags like `Ai` / `NPC limited` to indicate AI slots
- For a 100-player server with 30 AI slots: set `maxplayers` appropriately and use ~30 as **BonusPlayers** so the browser shows the full expected population

---

## How It Works

Uses **Harmony Transpilers** to add `BonusPlayers` to the reported player count in every place the game sends it to browsers:

| Patch | What it affects |
|-------|------------------|
| `ServerMgr.UpdateServerInformation` | Steam GameTags `cp` and `SteamServer.BotCount` |
| `CompanionServer.Handlers.Info.Execute` | App / Companion server info (some browser sources) |
| `Rust.Nexus.Handlers.PingHandler.Handle` | **In-game server list** (Nexus ping response) |

Each replaces `BasePlayer.activePlayerList.Count` with `BasePlayer.activePlayerList.Count + BonusPlayers` at the injection point. Only the values sent to server browsers are modified; all other game logic (slots, queues, etc.) uses the real count.

---

## dnSpy Method (Legacy)

The old approach of editing `Assembly-CSharp.dll` with dnSpy still works conceptually but:
- Line numbers change every Rust update (the guide said line 1090)
- You must re-apply after each game update
- Harmony mods survive updates better and can be toggled without replacing DLLs

---

## Compatibility

- Load order: No special requirements
- Other mods that patch `ServerMgr.UpdateServerInformation` (e.g. BetterAirDrop, CraftingSpeed, BagCooldowns) can run alongside this; Harmony merges patches

---

## Known behavior: Session vs Play browser

- **Loading screen and Session tab:** Read GameTags `cp`. The mod rewrites that, so you see the inflated count (e.g. 30/100).
- **Play → Community list and the server card:** Facepunch downloads a server-list snapshot and **clamps** the displayed count to Steam’s authorized player count (`clampPlayerCountsToTrustedValues`). That map is `https://api.facepunch.com/api/public/steamServers/playerCounts/rust` (this host:port → real connections only). GameTags, A2S bots, and unauthenticated Steam sessions do not change it.

There is no dedicated-server Harmony hook that can raise Facepunch’s trusted Steam count without real Steam-authenticated connections.

---

## Troubleshooting

**Play Community still shows the real count:**

That is expected. Facepunch clamps Play to Steam’s authorized player count. Loading screen and Session will still show `BonusPlayers`.
