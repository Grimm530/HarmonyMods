# MapVoter (Harmony Mod)

A **Harmony mod** for map voting on Rust servers **without Oxide**. Converted from the Oxide MapVoter plugin to work standalone.

## Purpose

- Players vote for the next map via an in-game UI
- Admin runs **mvote** or **mvoteready** to randomly pick maps from the local image pool, post them to Discord, and open voting
- Players use **vote** (or **mvote** while a vote is active) to open the UI
- Scheduled auto-vote uses the same pool pick + Discord post when the config window opens
- Uses vanilla CUI (`CommunityEntity`)—no Oxide `CuiHelper`
- Config: `HarmonyConfig/MapVoter.json` (in server root)

## Requirements

- Rust dedicated server with Harmony mod support (e.g. Rust.Harmony)
- No Oxide/uMod required

## Installation

1. **Build the mod**
   ```powershell
   cd .cursor/HarmonyMods/MapVoter
   .\build.ps1
   ```

2. **Copy the DLL** to `HarmonyMods/` folder in your server root

3. **Configure** `HarmonyConfig/MapVoter.json`. For procedural map voting:
   - Set `"Map size": 4000` (or 3500, 4500, etc.)
   - Put map preview images in **Images path** (this server: `maps/images`). Name them **`{size}_{seed}.png`** or **`.jpg`**, e.g. `4000_523577557.png`.
   - Set `"Number of maps to show (random seeds)": 8` — each vote randomly picks that many maps **from the image pool**. Add more images over time; the pool can grow.
   - **mvote** / **mvoteready**: Picks 8 random maps from the pool, loads those images, posts to Discord, and opens the in-game vote.
   - **Auto Vote**: When enabled, the same pool pick + Discord post runs on the schedule (`Start voting X days before wipe` at `Vote start (HH:mm)`).
   - Optional: `"Images path"` override (default: server root/HarmonyImages/MapVoter)
   - For a fixed manual list instead, set `"Map size": 0` and use `"Map options (manual list - used when Map size is 0)"`

4. Restart server or `harmony.load MapVoter`

## Usage

- **mvote** (chat or console): Admin starts a vote by randomly picking maps from the image pool, loading those images, and posting to Discord. Opens the UI. If a vote is already open, just opens the UI.
- **mvoteready** (admin): Same as starting a vote from the pool. If a vote is already open, re-posts the current maps to Discord.
- **Open voting UI** (players): Type the configured command (e.g. `vote` or `mapvote`) in chat to open the map voting panel and cast your vote.
- **mvtest** / **mapvotestart** (admin): Same as **mvote** — pick from pool, post to Discord, open the vote.
- **mvotediscord** (admin, server console): Resend the current vote status to Discord (useful if the bridge was down or the initial send failed).
- Click **VOTE** on a map to cast your vote; close button destroys the UI

## CUI Reference

This mod follows the CUI pattern from `.## CUI (Community UI) Reference Files.md`:
- JSON sent via `CommunityEntity.ServerInstance.ClientRPC(AddUI, json)`
- Buttons use `cui.endtest SENDCMD MapVoter_XXX` and are intercepted by a Harmony patch
- No Oxide `CuiHelper` or `CuiElement`—pure JSON + `CommunityEntity`

## Discord Integration (ticket-support-system)

MapVoter logs to Discord via the **ticket-support-system** `mapvoterDiscordBridge`:

1. The ticket-support-system runs `mapvoterDiscordBridge` (HTTP server on port 3921) when the bot starts
2. MapVoter Harmony mod POSTs vote events to `{BridgeUrl}/mapvoter`
3. The bridge posts embeds to the configured Discord channels (Vote Channel, Winning Map Channel, Logs)

**Map images in Discord:** Discord shows a map image in each vote box when that file exists in the **Images path** (config: `"Images path"`, e.g. `maps/images`). Name them **`{size}_{seed}.png`** or **`.jpg`**. Vote start picks from that pool; MapVoter sends resized JPEG/PNG as base64 to the bridge (max dimension default 512px).

**When Discord messages are sent** (and when they are NOT):
- **vote_started** – When you run **mvote** / **mvoteready** (picks from the image pool and posts map cards); when auto-vote starts on schedule; or when you run `mvotediscord` while a vote is active
- **vote_ended** – When the vote actually closes (wipe timer or manual stop); or when you run `mvotediscord` after a vote has already ended
- **Not sent** on plugin load, plugin unload, or when restoring a vote from file after a server restart (use `mvotediscord` to resend if needed)

`mvotediscord` only resends the current vote state to Discord—it does not start or end votes.

**Discord vote buttons** – Clicks **count** only if the voter is **verified** on Platform Sync (`linkingSystem: 3` in the bot). The bot resolves **SteamID** via `link.platformsync.io`, then sends RCON **`global.discordvote <steam64> <mapIndex>`** using the same **Rustcord** server list (`rustcord_relay.servers`). Unverified users get: *You need to be Verified to vote* and a link to `channel_ids.mapvote_verify_channel_id` in the bot config.

Vote tallies persist in `HarmonyData/MapVoter/current_vote_state.json`; optional audit lines in `HarmonyData/MapVoter/discord_steam_vote_audit.log`. **Do not** put votes in `current_vote_seeds.txt` (that file is only the map seed list).

Enable in config: `"Log to Discord (true/false)": true` and set channel IDs + Bridge URL. Bot: `mapvoter_bridge` → `enabled: true`, `port: 3921`; RCON targets default from `rustcord_relay.servers`.

## vs Original Oxide MapVoter

This is a **simplified** Harmony port. The original MapVoter has:
- Discord integration
- ImageLibrary, RustMaps API, WipeInfoApi, ServerRewards, Kits
- Auto-wipe, auto-vote scheduling
- Complex procedural/custom map support

This version provides:
- Basic map list from config or procedural seeds
- **RustMaps API v4** for map preview images (API key required)
- Vote recording
- CUI voting UI (2×4 grid of map cards)
- Chat command to open UI

Extended features: Discord bridge (implemented), **auto-wipe (implemented)**.

### Auto-Wipe

When `"Enable Auto Wipe (true/false)"` is `true`, MapVoter:

1. **Uses a first-Thursday calendar** (not Facepunch WipeTimer): forced wipe on the first Thursday of each month at **Forced Wipe time** (e.g. 13:45), then map wipes every **14 days** after that Thursday at **Wipe time** (e.g. 17:30), skipping any 14-day slot that would land on or after the next forced wipe (long months get a second map wipe; then forced wipe is ~7 days later)
2. **Schedules restart** when that event is within the configured window (default 120 minutes)
3. **Uses the vote winner** as the next map if a vote is active; otherwise uses a random seed
4. **Updates server.cfg** on shutdown (seed, worldsize, or levelurl for custom maps) before the server quits
5. **Server data wipe** (optional): On the next load after a wipe, deletes old map/save files from `server/{identity}/` if enabled

Auto-vote (4 days before at 17:00) uses the same next-wipe datetime. Console: `mvotewipes` prints the upcoming calendar.

**Ramping interval**: >2h to wipe = every 1h; 30min–2h = every 15min; <30min = every 2min. Checks keep running even when the next wipe is more than 32h away.

**Config** (`Auto Wipe` section):

- `Forced Wipe time`: first Thursday of the month (Facepunch forced wipe)
- `Wipe time`: 14-day Thursday map wipes
- `Map wipe interval (days after first Thursday, repeats until next forced wipe)`: `14`
- `Server identity`: Server folder name (e.g. `grimm` → `server/grimm/cfg/server.cfg`)
- `Schedule restart when within (minutes) of wipe`: Act when wipe is this many minutes away (default 120)
- `Custom Map` / `Custom map URL`: Use a custom map URL instead of procedural

**Server data wipe** (optional, in `Server data wipe` section): Deletes old map files, player data, etc. from `server/{identity}/` on load after a wipe. Uses substring patterns (e.g. `proceduralmap` matches `proceduralmap.4000.239.281.map`). The NEW procedural map is never deleted—MapVoter skips files matching the new seed/size. Patterns: `proceduralmap`, `player.deaths`, `player.identities`, `player.states`, `player.tokens`, `relationship`, `sv.files`, `companion.id`.

**Logs wipe** (optional): Deletes prior-day log files from `logs/` (e.g. `logfile-20260218-151044.txt`). Keeps today's log to avoid deleting the active file.

**Oxide wipe** (optional): Only runs if `oxide/` folder exists. Deletes `oxide/logs` folder and configured `oxide/data/` files (e.g. `oxide.covalence.data`, `oxide.lang.data`).

*Note: The original Oxide MapVoter had "Plugins Data wipe" for `oxide/data` (other plugins' data). This Harmony version uses Server data wipe (server/grimm), Logs wipe, and Oxide wipe instead.*

## Image Processing

Map preview images are resized and JPEG-compressed before being sent to the voting UI. Large PNG files (~10MB) are reduced to ~1MB by:
- Resizing to max 768px on the longest edge (configurable)
- Encoding as JPEG at 75% quality (configurable)

Config options in `HarmonyConfig/MapVoter.json`:
- `"Map image max dimension (resize/compress for UI - default 768)"`: 256–2048
- `"Map image JPEG quality (0-100, default 75)"`: 50–95 (lower = smaller file)
- `"Discord image max dimension (smaller = smaller payload - default 512)"`: 256–1024 (images sent to Discord are resized to this to keep POST size down)

Original files on disk are not modified; compression happens when loading into FileStorage for the UI.

## Image pool (random pick)

MapVoter does **not** invent new random seeds at vote time. It scans the **Images path** for files named `{size}_{seed}.png` / `.jpg` matching `"Map size"`, then randomly picks `"Number of maps to show"` (default 8). Keep adding images; the pool can grow and each vote still shows 8.

## Local Images (External Generation)

To grow the pool without RustMaps API limits, generate extra map images with **CustomMapGen** and a batch script:

1. **MapVoter config**: Set `"Use local images only (no RustMaps API...)": true`.
2. **CustomMapGen config** (`HarmonyConfig/CustomMapGen.json`): Enable MapImage with:
   - `MapImage.Enabled`: true
   - `MapImage.OutputFolder`: your MapVoter Images path (e.g. `maps/images`)
   - `MapImage.MapVoterFormat`: true
3. **Run the generator script** (while main server is stopped, or use a separate server copy):
   ```powershell
   .\GenerateMapImages.ps1 -ServerRoot "C:\svr1"
   ```
4. Copy generated `{size}_{seed}.png` files into the Images path. Then **mvote** / auto-vote will include them in the random pool.

## Persisted Vote Seeds

The vote seed list is stored in `HarmonyData/MapVoter/current_vote_seeds.txt`:

- **File format**: First line = map size, following lines = one seed per line
- **On mvote** / **mvoteready** / auto-vote: Random maps are picked from the image pool and written to the file; vote is opened and posted to Discord
- **On server restart**: If the file exists and matching images are still on disk, the vote is restored (Discord is **not** auto-posted; use `mvotediscord` to resend)
- **On vote end** (wipe timer or manual stop): The file is deleted

**Manually changing a seed**: Unload the mod (`harmony.unload MapVoter`), edit `HarmonyData/MapVoter/current_vote_seeds.txt` to change a seed number, ensure the corresponding image exists (e.g. `4000_12345678.png` or `.jpg`) in the Images path, reload (`harmony.load MapVoter`), then run **mvoteready** again if you need to repost to Discord.

## File Locations

- **Source**: `.cursor/HarmonyMods/MapVoter/`
- **Output**: `HarmonyMods/MapVoter.dll`
- **Config**: `HarmonyConfig/MapVoter.json` (server root)
- **Image generator**: `.cursor/HarmonyMods/MapVoter/GenerateMapImages.ps1`
- **Vote seeds** (persisted): `HarmonyData/MapVoter/current_vote_seeds.txt`
