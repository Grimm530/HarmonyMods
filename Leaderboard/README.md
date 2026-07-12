# Leaderboard (Harmony Mod)

Standalone Harmony mod for tracking core Rust game stats (no Oxide). Tracks resources gathered, buildings built, kills/deaths, NPC/animal kills, loot, craft, recycle, play time; persists to JSON; can relay to your bot for MySQL; optional Discord webhook.

## Mod Identity

| Item | Value |
|------|--------|
| **Purpose** | Core-game leaderboard: gather, build, kill, loot, craft, recycle, play time |
| **Entry point** | `LeaderboardMod` implements `IHarmonyModHooks` |
| **Loading** | `harmony.load Leaderboard` (from `HarmonyMods/Leaderboard.dll`) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `LeaderboardMod.cs` | Lifecycle, config, storage, relay, commands, Discord |
| `PlayerStats.cs` | Per-player stats (UserId, play time, StatsStorage by LootType) |
| `LootTypeEnum.cs` | Stat categories (Gather, Kill, Death, Construction, Upgrade, etc.) |
| `LeaderboardConfig.cs` | Config model (storage, relay URL, Discord webhook) |
| `SteamIdHelper.cs` | `IsSteamId(ulong)` for filtering NPCs |
| `LeaderboardUI.cs` | Minimal CUI panel (title, close); Fullscreen template later |
| `LeaderboardTickBehaviour.cs` | MonoBehaviour for relay batch + Discord timer |
| `Storage/` | `ILeaderboardStorage`, `JsonLeaderboardStorage` |
| `Relay/RelaySender.cs` | HTTP POST stat updates (or batch) to bot |
| `Discord/DiscordHelper.cs` | Webhook POST for embeds |
| `Patches/` | Harmony patches for game events |

## Data & MySQL (Option 2: Relay to Bot)

- **Local:** Stats stored in `HarmonyMods_Data/LeaderboardData/Players/<steamid>.json` (config: `DataFolder`, `StorageType`; default folder is `HarmonyMods_Data/LeaderboardData`).
- **Relay:** If `Relay.Enabled` and `Relay.Url` are set, stat updates are batched and POSTed as JSON in **the same format as the Oxide plugin’s MySQL tables** so your endpoint can write to the same DB. Payload: `Updates` (UserId, LootType, ShortName, ItemValue = **total**) and `Players` (UserId, LastIP, LastName, ConnectTime, DisconnectTime, TotalPlayTime, Points, HiddenFromLeaderboard). See `RELAY_ENDPOINT.md`.
- **Discord:** Optional `Discord.WebhookUrl` + `Discord.Enabled` + `Discord.AutoMessageIntervalSeconds` for periodic “Top 5 Kills” (or similar) embeds.

**Division of responsibility (with UltimateLeaderboard Discord bot):** This mod’s job is **only to get data to MySQL** (via Relay). The **bot** reads from the same database, uses its own template to build stats images, and posts to Discord (e.g. `/stats`). You do not need the mod to do any Discord or template work—the bot handles that.

## Commands

| Command | Purpose |
|---------|---------|
| `leaderboard`, `lb`, `stats` | Open leaderboard UI (console or chat) |
| `/leaderboard`, `/lb`, `/stats` | Same via chat (consumes message) |
| `leaderboard.close` | Close UI (used by CUI close button) |

## Config

- **Path:** `HarmonyConfig/Leaderboard.json` (next to server root). Player data is stored under `HarmonyMods_Data/LeaderboardData/Players/`.
- **Options:** `StorageType` (Json), `DataFolder`, `Commands`, `CooldownSeconds`, `Relay` (Enabled, Url, BatchIntervalSeconds), `Discord` (WebhookUrl, Enabled, AutoMessageIntervalSeconds), `TemplatePath`, `ImageBaseUrl`.
- **For “mod → MySQL, bot → Discord” setups:** You only need **Relay** (Enabled, Url, BatchIntervalSeconds) so the mod sends data to your relay (e.g. the Discord bot), which writes to MySQL. The bot’s template and Discord posting are separate.
- **TemplatePath / ImageBaseUrl:** Used only for the **in-game** leaderboard panel (CUI) in Rust (e.g. fullscreen template, stat icons). They do not affect Discord; the bot has its own template and image logic.

## Harmony Patches

| Patch | Target | Purpose |
|-------|--------|---------|
| BaseCombatEntity.Die | Postfix | NPC/animal/heli/Bradley/block kills → attacker |
| BasePlayer.Die | Postfix | Deaths (victim), kills + max_distance (killer) |
| BasePlayer.GiveItem | Postfix | ResourceHarvested → **Gather** (stone, ore, etc.); Crafted + fish → **Fishing** |
| BasePlayer.LifeStoryShotFired | Postfix | **ShotFired** (ammo type per shot when firing projectile weapons) |
| BaseEntity.OnPlaced | Postfix | Construction |
| BuildingBlock.SetGrade | Postfix | Upgrade |
| ItemCrafter.FinishCrafting | Postfix | Craft |
| Recycler.MoveItemToOutput | Postfix | RecycleItem |
| PlayerLoot.StartLootingEntity | Postfix | **Crate** (container type) when opening any crate/box (LootContainer or StorageContainer) |
| Item.MoveToContainer | Prefix/Postfix | **LootItems** when you take items from a crate/box into your inventory (manual looting) |
| WorldItem.Pickup | Prefix/Postfix | **LootItems** when a world item is picked up (e.g. InstantBarrel barrel loot, ground pickups) |
| LootContainer.DropItems | Prefix/Postfix | **LootItems** when barrel (etc.) is broken → items credited to attacker |
| BaseCombatEntity.Hurt(HitInfo) | Postfix | **PvP hit tracking** → BodyHits (head, chest, stomach, arm, leg) for hitrate charts |
| TimedExplosive.Explode(Vector3) | Postfix | **ExplosiveUsed** (satchel, C4, beancan, F1, molotov, flashbang, survey charge, rockets, GL HE, MLRS) when they explode → raid stats |
| BasePlayer.ServerInit | Postfix | Connect → play time start |
| BasePlayer.OnDestroy | Prefix | Disconnect → play time end, save |
| Chat.say | Prefix | `/lb`, `/leaderboard`, `/stats` → open UI |

## Leaderboard categories → data source

Each in-game category is filled from specific **LootType**s and **recording methods** (Harmony patches). Data is stored in `StatsStorage[LootType][shortname]` and displayed in the Resources tab sections.

| Category | LootType(s) | Recorded by (patch / game path) |
|----------|-------------|----------------------------------|
| **My Statistics** | Custom (derived) | K/D, play time, total resources, total crafted, structures, upgrades, events from other stats |
| **Resources** | Gather, LootItems | **BasePlayer.GiveItem** (ResourceHarvested) → stone, wood, ores, leather, bone, fat, scrap |
| **Looted** | Crate, LootItems | **PlayerLoot.StartLootingEntity** → Crate; **Item.MoveToContainer** + **WorldItem.Pickup** + **LootContainer.DropItems** → LootItems (crates/boxes, pickups, barrels) |
| **Crafted** | Craft | **ItemCrafter.FinishCrafting** → each completed craft (shortname + amount) |
| **Fishing** | Fishing | **BasePlayer.GiveItem** (Crafted + item shortname `fish.*` or `skull.human`) → caught fish |
| **Fired** | ShotFired | **BasePlayer.LifeStoryShotFired** → ammo shortname per shot (rifle, pistol, shotgun, arrows, etc.); MLRS uses different path (not currently recorded) |
| **Recycled** | RecycleItem | **Recycler.MoveItemToOutput** → items received from recycler (shortname + amount) |
| **Raid** | ExplosiveUsed (and ShotFired for explosive ammo) | **TimedExplosive.Explode** → satchel, C4, beancan, F1, molotov, flashbang, survey charge, rockets, GL HE, MLRS when they explode |
| **Misc** | Kill | **BaseCombatEntity.Die** → animal/NPC/heli/Bradley kills (bear, boar, wolf, helicopter, bradleyapc, etc.) |
| **Farming** | Gather | **BasePlayer.GiveItem** (ResourceHarvested) → hemp, berries, potato, cloth, mushroom, corn, pumpkin, flowers, wheat |
| **Construction / Upgrade** | Construction, Upgrade | **BaseEntity.OnPlaced** → buildings; **BuildingBlock.SetGrade** → upgrades (wood, stone, metal, armored) |
| **Kills / Deaths / PvP** | Kill, Death, BodyHits | **BasePlayer.Die** → deaths + killer’s kills/max_distance; **BaseCombatEntity.Hurt** → PvP body hits (hitrate) |

So: **Resources** and **Farming** both use **Gather** (different keys); **Looted** uses **Crate** + **LootItems**; **Fired** uses **ShotFired**; **Raid** uses **ExplosiveUsed** (and some **ShotFired** in UI). All of these are produced by the patches above.

## UI

- **Current:** Minimal panel (background, title “Leaderboard”, close button). Fullscreen template (Fullscreen.json + Settings, Awards, TopPlaces) can be loaded from `LeaderboardData/Templates/` in a future update.
- **CUI:** Built with `CommunityEntity.ServerInstance.ClientRPC("AddUI", connection, json)` and `DestroyUI` by name.

## Build & Deploy

```powershell
cd .cursor\HarmonyMods\Leaderboard
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\Leaderboard.dll`. Load: `harmony.load Leaderboard`.

## PvP & hitrate

- **Kills/deaths:** Recorded when a player dies (Patch_BasePlayer_Die). Charts update on kill/death.
- **Hit damage / hitrate:** When you shoot or melee another player, each hit is recorded by body part (Patch_BaseCombatEntity.Hurt). The **Hitrate** tab shows your PvP hit distribution (HEAD, CHEST, STOMACH, ARM, LEG) as a percentage of your total hits on players.

## Compatibility (e.g. BetterBackpack, InstantBarrel)

- **Crates and boxes:** Opening a crate/box records **Crate** once. **LootItems** are recorded when you actually move items from that container into your inventory (`Item.MoveToContainer`), so manual looting from crates, toolboxes, and deployable boxes is fully counted.
- **Barrel loot:** InstantBarrel injects loot as world items and triggers `WorldItem.Pickup`, which is patched—barrel scrap etc. is counted. Vanilla barrel break uses `LootContainer.DropItems`, also patched.
- **BetterBackpack:** Moving items into a backpack does not bypass counting; items were already credited when taken from the container. No change needed for BetterBackpack.

## Requirements

- Rust dedicated server with Harmony loader (e.g. Oxide Harmony-Assembly or equivalent).
- References: Rust.Data, Facepunch.System, Rust.Harmony, 0Harmony, Assembly-CSharp, Newtonsoft.Json, Facepunch.Console, Facepunch.Network, Facepunch.UnityEngine, UnityEngine.CoreModule, UnityEngine.UnityWebRequestModule, Rust.Global (paths in .csproj assume `RustDedicated_Data\Managed`).
