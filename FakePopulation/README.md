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

- **Session tab (when you’re connected):** Shows the inflated count (e.g. 30/100). The client gets this from server info we patch (GameTags/keys), so it’s correct.
- **Play → Server browser (main menu, before joining):** May still show 0/100. That list is often filled from Steam’s A2S_INFO or another source that reports the **real connection count**. We patch GameTags (`cp`) and set `SteamServer.BotCount`, but the Play browser may only display the main “players” field (actual connections), which the Steam/backend stack sets from real connections. There is no in-game hook to override that from a Harmony mod without patching the Steamworks layer.

So the mod is working where the game uses our values (Session, and any browser that reads GameTags/bot count). The main-menu Play list can still show 0 due to how that list is populated.

---

## Troubleshooting

**In-game server list or Steam still shows 0 players (mod loads but count is real/zero):**

- The mod uses (1) a transpiler on `cp` in GameTags and (2) `SteamServer.BotCount` as fallback. If the in-game list still shows 0, check the log for `Transpiler could not find injection point` — after a Rust update the game IL may change; only the BotCount fallback runs, and the in-game browser may ignore it.
- **Steam client vs in-game browser:** The Steam "Game Servers" window uses A2S_INFO; the in-game Rust list may use GameTags. If only one place shows the inflated count, that's expected.
- **Refresh:** Steam may cache; try switching tabs or waiting 30+ seconds.
- **Config:** Ensure `HarmonyConfig/FakePopulation.json` has `BonusPlayers` > 0 (e.g. 30). Path is relative to server root.
- **Steam client vs in-game browser:** The Steam client’s “Game Servers” window uses A2S_INFO; the in-game Rust list may use GameTags. If only one shows the inflated count, that’s expected.
