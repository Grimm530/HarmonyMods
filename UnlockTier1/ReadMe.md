# UnlockTier1 — Dictionary / Reference

Persistent reference for AI when modifying, extending, or debugging the **UnlockTier1** Harmony mod.

**Mod type:** Harmony mod. Loaded by HarmonyLoader from `HarmonyMods/`. No Oxide lifecycle hooks.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | UnlockTier1 |
| **Author** | nivex |
| **Version** | 1.0.0.0 |
| **Type** | Harmony mod |
| **Purpose** | Unlocks all Tier 1 (workbench level ≤ 1) craftable blueprints for players on join and on mod load for already-connected players |

**Primary responsibilities:**
- On load: run initialization only when `Server.identity != "my_server_identity"` (guard for a specific server identity)
- On Bootstrap.StartupShared (Postfix): log load, then unlock Tier 1 blueprints for every player in `BasePlayer.activePlayerList`
- On BasePlayer.PlayerInit (Postfix): unlock Tier 1 blueprints for the connecting player
- Expose `UnlockBlueprints(ulong userid, int itemid = 0)` for unlocking blueprints by user ID (e.g. from another mod or tooling)

**Key behavior:** Only blueprints that are `userCraftable`, not `defaultBlueprint`, and have `workbenchLevelRequired <= maxTier` (default 1) are added to `PersistantPlayerInfo.unlockedItems`. DLC-only blueprints are excluded in `UnlockBlueprints` via `!blueprint.NeedsSteamDLC`.

---

## 2) Project Structure & Topology

| Path / component | Purpose |
|------------------|--------|
| `.cursor/HarmonyMods/UnlockTier1/` | Source root |
| `HarmonyMods.RustGame.Nivex.UnlockTier1/Manager.cs` | Single entry: `IHarmonyModHooks` (OnLoaded / OnUnloaded), nested patch classes `Bootstrap_StartupShared`, `BasePlayer_PlayerInit`, and helpers `UnlockTier`, `UnlockBlueprints` |
| `UnlockTier1.csproj` | Targets net48; references Rust.Harmony, 0Harmony, Assembly-CSharp, Facepunch/Unity/Rust.Data |
| `Properties/AssemblyInfo.cs` | Assembly version 1.0.0.0, title UnlockTier1 |

**Config path:** None. Mod has no config file.

**State flow:** OnLoaded (with identity guard) → `Bootstrap_StartupShared.Initialize()` → unlock Tier 1 for all active players. Each new player init → Postfix calls `UnlockTier(player)`. Persistence is game-native (`PersistantPlayerInfo`, `ServerMgr.persistance`).

---

## 3) Persistent Data Model

- **No mod-owned persistent files.** All state is the game’s `PersistantPlayerInfo` (e.g. `unlockedItems`) and `ServerMgr.persistance` (GetPlayerInfo / SetPlayerInfo).
- **UnlockTier:** Updates `player.PersistantPlayerInfo.unlockedItems`, assigns back to player, `SendNetworkUpdateImmediate`, then `ClientRPC(UnlockedBlueprint, 0)` so the client refreshes.
- **UnlockBlueprints:** Reads/updates `persistance.GetPlayerInfo(userid)`; if the player had no unlocks before, calls `SetPlayerInfo`; if `BasePlayer` is online, syncs `PersistantPlayerInfo` and sends network update + ClientRPC.

---

## 4) Configuration Schema

None. Mod has no configuration file or schema. Tier is hard-coded as `maxTier = 1` in `UnlockTier(BasePlayer, int maxTier = 1)`. Server identity guard is the literal string `"my_server_identity"` in `OnLoaded`.

---

## 5) Console Commands

None. Mod does not register console commands. `UnlockBlueprints(ulong, int)` is internal static and could be invoked by other code (e.g. another mod) but is not exposed as a command here.

---

## 6) Harmony Patches & Event Flow

| Patch target | Patch type | Purpose |
|--------------|------------|---------|
| **Bootstrap.StartupShared** | Postfix | Call `Initialize()`: log "[Harmony] Loaded: UnlockTier1 1.0.0.0 by nivex", then for each `BasePlayer` in `BasePlayer.activePlayerList` call `BasePlayer_PlayerInit.UnlockTier(current)`. |
| **BasePlayer.PlayerInit(Connection)** | Postfix | If `c != null` and `c.player` is `BasePlayer`, call `UnlockTier(player)`. Exceptions are swallowed. |

**UnlockTier:** Iterates `ItemManager.GetBlueprints()`; for each blueprint with `userCraftable && !defaultBlueprint && blueprint.workbenchLevelRequired <= maxTier` and not already in `persistantPlayerInfo.unlockedItems`, adds `blueprint.targetItem.itemid`. Then assigns `PersistantPlayerInfo`, `SendNetworkUpdateImmediate`, and `ClientRPC(UnlockedBlueprint, 0)`.

**UnlockBlueprints:** Takes `userid` and optional `itemid` (0 = all). Uses `ItemManager.GetBlueprints()` with `userCraftable && !NeedsSteamDLC` and optional item filter; adds missing blueprints to `playerInfo.unlockedItems`. If the player had no unlocks before, calls `persistance.SetPlayerInfo`. If `BasePlayer.FindByID(userid)` is non-null, syncs and sends ClientRPC.

---

## 7) Lifecycle & State Machine

- **OnLoaded:** If `ConVar.Server.identity != "my_server_identity"`, call `Bootstrap_StartupShared.Initialize()` (log + unlock Tier 1 for all active players). No config load; no PatchAll in this mod (HarmonyLoader applies patches from the assembly).
- **OnUnloaded:** Log "[Harmony] Unloaded: UnlockTier1 1.0.0.0 by nivex". No explicit entity or timer cleanup; unlocked blueprints remain in game persistence.
- **Runtime:** Each `BasePlayer.PlayerInit(Connection)` Postfix runs for new connections and calls `UnlockTier(player)`.

---

## 8) What NOT to Touch Without Care

- **Patch targets:** `Bootstrap.StartupShared`, `BasePlayer.PlayerInit(Connection)` — method names/signatures can change with Rust version.
- **Server identity guard:** The check `Server.identity != "my_server_identity"` controls whether initialization runs; changing or removing it changes which servers get auto-unlock on load.
- **Blueprint iteration:** `ItemManager.GetBlueprints()` and conditions `userCraftable`, `defaultBlueprint`, `workbenchLevelRequired` are game APIs; renames or logic changes in the game can break the mod.
- **Persistence and RPC:** `PersistantPlayerInfo`, `SendNetworkUpdateImmediate`, and `UnlockedBlueprint` ClientRPC are game contracts; changing them can desync or break saving.
- **UnlockBlueprints and SetPlayerInfo:** Only call `SetPlayerInfo` when the player had no unlocks before (`flag`); other call patterns could overwrite or conflict with game logic.

---

## 9) Performance Anti-Patterns

- **Reference:** `.cursor/PluginInstructionalFiles/#System.Linq-Removal.md`, `Rust_Plugin_Performance_Best_Practices.md`.
- **GetBlueprints():** Called once per player in `UnlockTier` and once per `UnlockBlueprints` call; avoid calling in tight loops or from frequently run code.
- **activePlayerList:** Initialize() iterates `BasePlayer.activePlayerList` once at load; acceptable. Do not iterate `BaseNetworkable.serverEntities` for player lookup — use `BasePlayer.FindByID(userid)` when you have an ID (as in `UnlockBlueprints`).
- No LINQ in the current implementation; keep hot paths free of LINQ when extending.

---

## Workspace Paths (this project)

| Path | Purpose |
|------|---------|
| `.cursor/HarmonyMods/UnlockTier1/` | UnlockTier1 source |
| `HarmonyMods.RustGame.Nivex.UnlockTier1/Manager.cs` | Only source file (patches + hooks) |
| `HarmonyMods/` (runtime) | Deployed Harmony DLLs |
| `bin/Release/net48/UnlockTier1.dll` | Build output (AssemblyName: UnlockTier1) |
