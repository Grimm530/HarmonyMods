# Convoy (Harmony Mod)

Harmony port of the Convoy Harmony mod: **convoystart** / **convoystop** commands, map marker, and Convoy-style **vanilla NPC kits** and **loot rules**. Uses Loot Settings, NPC/Crate presets from **Convoy.json** (same file as Oxide Convoy). Harmony-only dependency. See **HARMONY_MODS_GUIDE.md** → “Convoy-Style Vanilla NPC Kits and Loot”.

## Identity

| Field | Value |
|-------|--------|
| **Name** | Convoy |
| **Type** | Harmony mod (IL patching) |
| **Config** | `Convoy.json` (Loot Settings, NPC Configurations, Crate presets, prefix) |
| **Load** | `harmony.load Convoy` |

## Commands

| Command | Description |
|---------|-------------|
| **convoystart** | Start convoy: from **server console** (no player) uses default event position; from **chat/F1** (as admin) uses your position. Creates map marker and sets state. |
| **convoystop** | Stop convoy: removes map marker, clears state. From server console or as admin. If auto-event is on, next event is scheduled on timer. |

- **Server console**: no player required; convoy starts at **Default event position (x,y,z)** from config.
- **In-game (chat/F1)**: admin required; marker at your position.
- The Harmony mod creates the map marker and state only. **Full convoy (vehicles roaming roads)** requires the Oxide Convoy plugin or a full port.

## Features

- **Commands** – `/convoystart` and `/convoystop` (admin-only) with chat feedback.
- **Map marker** – On convoystart, spawns the vending map marker at admin position (“Convoy (Harmony)”); convoystop removes it.
- **Loot Settings** – Loot on car destroy (Y/N), loot loss %, prohibit options, **Event lock: damage threshold** (e.g. 500 = lock event to attacker team), **Event lock: unlock after no damage for (seconds)** (e.g. 900 = 15 min).
- **NPC presets** – Apply Convoy-style kits (Health, Wear, Belt, Scale damage, Speed) to vanilla scientists; register for corpse loot table.
- **Corpse loot** – Extra loot from preset “Own loot table” (item list) when a registered NPC dies.
- **Convoy crates** – Register crates as convoy (state/API). **Event lock:** when a player/team deals 500 damage (configurable) to convoy entities, the entire event locks to that team—only they (and teammates) can loot NPC loot, crates, and hackable locked crates. Lock clears if that team deals no damage for 15 minutes (configurable). When not locked, TruePVE/Loot Defender handle protection when present.
- **State** – Set convoy state (moving, NPCs/Bradley/Heli alive) so prohibit rules apply.

## Config paths (first found)

- `legacy/config/Convoy.json`
- `Config/Convoy.json`
- `HarmonyConfig/Convoy.json`
- `Convoy.json` (server root)

Same file as the Oxide Convoy plugin; the mod uses **Loot Settings**, **NPC Configurations**, **Crate presets**, **Prefix**, **Main Setting**, and the keys below. Other keys are ignored.

- **Main Setting** – Timer-based auto start (no player): `Enable automatic event holding [true/false]`, `Minimum time between events [sec]`, `Maximum time between events [sec]`. When enabled, the mod starts an event on a timer (map marker + state) and optionally auto-stops after **Event duration when auto-started [sec]** (0 = run until convoystop).
- **Default event position (x,y,z)** – Used when starting from server console or auto-timer (e.g. `[0, 100, 0]`).
- **Event duration when auto-started [sec]** – After an auto-started event, auto-stop and reschedule timer. `0` = event runs until **convoystop**.
- **Marker Config** – **Do you use the Marker?**, **Use a shop marker?**, **Use a circular marker?**, **Radius**, **Alpha**, **Marker color**, **Outline color**. **Radius** uses the same scale as the Harmony mod: use **0.2** for a small ring; large values (e.g. 50) make the ring cover the whole map.
- **Enable debug logging [true/false]** – When `true`, logs to the server console: config path, command registration, every command invocation (player, admin, position), and map marker creation. Set to `false` in production.

**Pathfinding / routes:** The Harmony mod does not implement pathfinding, route generation (PathConfig, Route Settings), or spawning vehicles on roads. That logic lives in the Oxide Convoy plugin (PathManager, EventController, Convoy Presets, vehicle configs). For vehicles roaming roads, use the Oxide Convoy plugin; this mod provides markers, timer, state, and kit/loot rules only.

## API (for other Harmony mods or plugins)

- **ConvoyState.RegisterConvoyCrate(ulong netId)** – Mark a crate as convoy (state/API). Looting protection when present is handled by TruePVE/Loot Defender, not by this mod.
- **ConvoyState.UnregisterConvoyCrate(ulong netId)** – Unmark.
- **ConvoyState.SetConvoyState(bool moving, bool npcsAlive, bool bradleyAlive, bool heliAlive)** – Set state for prohibit rules.
- **ConvoyApplier.ApplyNpcPreset(BaseCombatEntity npc, string presetName)** – Apply kit and register NPC for corpse loot.
- **ConvoyApplier.PopulateCorpseFromPreset(LootableCorpse corpse, ulong sourceNpcNetId)** – Add preset “Own loot table” to corpse (e.g. from OnEntityDeath / corpse spawn).
- **ConvoyApplier.AddLootFromTable(ItemContainer container, LootTableEntry table)** – Add items from a loot table to a container.

## Patches

| Target | Effect |
|--------|--------|
| `ConsoleSystem.Index.Server.Find` | Convoy command registration. |
| `BaseCombatEntity.Hurt(HitInfo)` | Record damage to convoy entities per team; when a team reaches the damage threshold, lock the event to that team. |
| `BaseCombatEntity.Die(HitInfo)` | When a convoy NPC dies, mark so the next LootableCorpse spawn is registered as a convoy corpse. |
| `BaseNetworkable.Spawn` | When a LootableCorpse spawns after a convoy NPC death, register it as a convoy corpse. |
| `PlayerLoot.StartLootingEntity` | When the event is locked to a team, only that team can loot convoy entities (crates, NPC corpses); otherwise no Convoy block. |
| `HackableLockedCrate.RPC_Hack` | When the event is locked, only the locked team can start hacking convoy hackable crates. |

**Event lock:** Once a team deals the configured damage (default 500) to convoy entities, the event locks to that team. Only that team can loot/hack convoy NPC loot, crates, and hackable locked crates. If that team deals no damage for the configured period (default 15 min), the event unlocks.

## Build / deploy

1. From repo: `./build.ps1` (or run from `Convoy` folder).
2. DLL is copied to `HarmonyMods/Convoy.dll`.
3. Place **Convoy.json** in one of the config paths above (can use the same Convoy.json as the Oxide Convoy plugin).

## Config alignment

- **Loot Settings** – Keys match Convoy.json “Loot Settings”.
- **NPC Configurations** – Preset Name, Name, Health, Wear items, Belt items, Speed, Scale damage, Own loot table.
- **Crate presets** – Preset Name, Prefab, SkinID, Own loot table.

Vehicle/slot → crate preset mapping is in HARMONY_MODS_GUIDE; when spawning crates call `ConvoyState.RegisterConvoyCrate(netId)` to register. Looting rules are left to TruePVE/Loot Defender when present.
