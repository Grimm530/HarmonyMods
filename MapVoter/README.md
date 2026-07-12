# MapVoter (Harmony Mod)

A **Harmony mod** for map voting on Rust servers **without Oxide**. Converted from the Oxide MapVoter plugin to work standalone.

## Purpose

- Players vote for the next map via an in-game UI
- Admin runs **mvote** to create the map list (seeds only); then **mvoteready** to post to Discord and open the vote so players can use the vote command (e.g. `vote`) to open the UI
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
   - **Two-phase workflow** (recommended when you add images manually):
     - **mvote** (or **mvtest** / **mapvotestart**): Only generates random seeds and writes them to `HarmonyData/MapVoter/current_vote_seeds.txt`. Responds with "Run mvoteready when ready to start voting." Does **not** post to Discord or open the vote. A copy is also written to `HarmonyImages/MapVoter/seeds_to_generate.txt` for reference.
     - Add map images (e.g. `4000_<seed>.png` or `.jpg`) to your **Images path** (default: `HarmonyImages/MapVoter` or config `"Images path"`).
     - **mvoteready** (or **mvotepost**): Loads maps from that seeds file and images from disk, posts to Discord (with images), opens the vote, and starts the wipe timer. Players can then use the configured vote command (e.g. `vote`) to open the in-game UI.
   - Optional: `"Images path"` override (default: server root/HarmonyImages/MapVoter)
   - For manual maps instead, set `"Map size": 0` and use `"Map options (manual list - used when Map size is 0)"`

4. Restart server or `harmony.load MapVoter`

## Usage

- **mvote** (chat or console): Creates the map list only (generates seeds, writes to `current_vote_seeds.txt`). Responds with **"Run mvoteready when ready to start voting."** Admin must run this first; then add images to the Images path.
- **mvoteready** (admin): When images are ready, run **mvoteready** (or **mvotepost**) to load maps from the seeds file, post to Discord, and open the vote. Players can then open the voting UI with the configured command (e.g. **vote** or **mapvote** – set in config `"Open MapVoter UI"`; default is `vote` if `mvote` is used for list creation).
- **Open voting UI** (players): Type the configured command (e.g. `vote` or `mapvote`) in chat to open the map voting panel and cast your vote. (If config is still `mvote`, use a different command like `vote` so **mvote** is reserved for creating the list.)
- **mvtest** / **mapvotestart** (admin): Same as **mvote** – seeds only; responds "Run mvoteready when ready to start voting."
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

**Map images in Discord:** Discord will show a map image in each vote box **only if** the image file exists on the Rust server when you run **mvoteready**. Put image files in the **Images path** (config: `"Images path"`, e.g. `maps/images` = server root `maps/images`). Name them exactly **`{size}_{seed}.png`** or **`.jpg`**, e.g. `4000_523577557.png`. After **mvote** the seeds are in `HarmonyData/MapVoter/current_vote_seeds.txt` – add one image per line (after the first line which is map size). Then run **mvoteready**. MapVoter sends them as base64 to the bridge; images are resized to the **Discord image max dimension** (default 512px) to keep the POST smaller.

**When Discord messages are sent** (and when they are NOT):
- **vote_started** – When you run **mvoteready** (or **mvotepost**) (loads images and posts to Discord with map cards); or when you run `mvotediscord` while a vote is active (resends current vote to Discord)
- **vote_ended** – When the vote actually closes (wipe timer or manual stop); or when you run `mvotediscord` after a vote has already ended (resends the end message to Discord)
- **Not sent** on **mvote** (seeds-only step), plugin load, plugin unload, or when restoring a vote from file after a server restart (use `mvotediscord` to resend if needed)

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

1. **Uses WipeTimer** (game's built-in wipe schedule) to know when the next wipe is
2. **Schedules restart** when the wipe is within the configured window (default: 120 minutes)
3. **Uses the vote winner** as the next map if a vote is active; otherwise uses a random seed
4. **Updates server.cfg** on shutdown (seed, worldsize, or levelurl for custom maps) before the server quits
5. **Server data wipe** (optional): On the next load after a wipe, deletes old map/save files from `server/{identity}/` if enabled. Uses substring patterns (e.g. `proceduralmap` matches `proceduralmap.4000.239.281.map`, `player.deaths` matches `player.deaths.10.db`)

**Load-time gate**: On each load, a single check runs. If wipe is **> 32 hours** away, no periodic checks run. On the next server restart (e.g. daily), it re-checks. Once within 32h, periodic checks start.

**Ramping interval** (when within 32h): >2h to wipe = every 1h; 30min–2h = every 15min; <30min = every 2min. Minimal checks over the wipe cycle.

**Config** (`Auto Wipe` section):

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

## Local Images (External Generation)

To avoid RustMaps API limits, you can generate map images locally using **CustomMapGen** and a batch script:

1. **MapVoter config**: Set `"Use local images only (no RustMaps API...)": true` if you use the API elsewhere.
2. **Generate seeds**: Run **mvote** (or `mvtest` / `mapvotestart`) – MapVoter writes seeds to `HarmonyData/MapVoter/current_vote_seeds.txt` and a copy to `HarmonyImages/MapVoter/seeds_to_generate.txt`. Responds: "Run mvoteready when ready to start voting."
3. **CustomMapGen config** (`HarmonyConfig/CustomMapGen.json`): Enable MapImage with:
   - `MapImage.Enabled`: true
   - `MapImage.OutputFolder`: `HarmonyImages/MapVoter`
   - `MapImage.MapVoterFormat`: true
4. **Run the generator script** (while main server is stopped, or use a separate server copy):
   ```powershell
   .\GenerateMapImages.ps1 -ServerRoot "D:\!RustServer"
   ```
5. The script starts RustDedicated for each seed, waits for CustomMapGen to save `{size}_{seed}.png`, then stops. Put the generated images in your MapVoter **Images path** (e.g. `D:\!RustServer\HarmonyImages\MapVoter`).
6. **Post and open vote**: Run **mvoteready** (or **mvotepost**) in server console – MapVoter loads images from disk, posts to Discord with map cards, and opens the in-game vote.

## Persisted Vote Seeds

The vote seed list is stored in `HarmonyData/MapVoter/current_vote_seeds.txt`:

- **File format**: First line = map size, following lines = one seed per line
- **On mvote** (or mvtest / mapvotestart): New random seeds are written to the file (no Discord post, vote not yet active)
- **On mvoteready** (or mvotepost): Seeds are read from the file, images loaded from the Images path, vote is opened and posted to Discord
- **On server restart**: If the file exists, the vote is restored from file (images loaded from disk); Discord is **not** auto-posted (use `mvotediscord` to resend)
- **On vote end** (wipe timer or manual stop): The file is deleted

**Manually changing a seed**: Unload the mod (`harmony.unload MapVoter`), edit `HarmonyData/MapVoter/current_vote_seeds.txt` to change a seed number, ensure the corresponding image exists (e.g. `4000_12345678.png` or `.jpg`) in the Images path, reload (`harmony.load MapVoter`), then run **mvoteready** again if you need to repost to Discord.

## File Locations

- **Source**: `.cursor/HarmonyMods/MapVoter/`
- **Output**: `HarmonyMods/MapVoter.dll`
- **Config**: `HarmonyConfig/MapVoter.json` (server root)
- **Image generator**: `.cursor/HarmonyMods/MapVoter/GenerateMapImages.ps1`
- **Vote seeds** (persisted): `HarmonyData/MapVoter/current_vote_seeds.txt`
