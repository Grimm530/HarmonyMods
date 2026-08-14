# Vanish Mod Markdown Creation Instructional


## Objective



## Vanish Mod Identity (Reference)

| Attribute | Value |
|-----------|--------|
| **Name** | Vanish |
| **Author** | Grimm530 |
| **Type** | Harmony mod |
| **Purpose** | Admin/privileged vanish: hide players from others (network/visibility), block AI targeting, optional damage block, noclip, safepoints, streamer mode, custom UI indicator |

**Primary responsibilities:**
- Toggle vanish per player (`Disappear` / `Reappear`); maintain `HiddenPlayers` and `VanishComponent` per vanished player
- Intercept console commands containing "vanish" (chat/console) for users with `authLevel != 0`
- Config: `HarmonyConfig/Vanish.json` (Users, CanSeeEveryone, AccessList, Messages); per-user `UserConfig` (effects, safepoints, UI image, damage block, etc.)
- **Permanent** vs **temporary** patches: temporary patches (e.g. `ShouldNetworkTo`, `Hurt`, `Die`) only applied when at least one player is vanished; unpatched when none
- Optional Oxide: when `Oxide.Core` is present, patch `Interface.CallHook(...)` so admins who are currently vanished can loot and authorize on cupboards

**Key flags / state:**
- `IsShuttingDown` — set in `ServerMgr.Shutdown` prefix; prevents `Reappear` on unload for already-disconnected players
- `config.CanSeeEveryone` — SteamIDs that always see vanished players (and receive snapshot updates); if empty, `BasePlayer_ShouldNetworkTo` is **not** applied
- `player.limitNetworking` — game flag used as "is vanished" indicator; `IsVanished(player)` = `player && player.limitNetworking && ID(player) > 76561197960265728L`

---

## Vanish Project Structure (Critical)

```
HarmonyMods/Vanish/
├── Vanish.cs / Vanish.csproj          # Root entry (optional/legacy)
├── build.ps1                         # Build script
├── HarmonyMods.RustGame.Vanish/
│   └── Manager.cs                    # IHarmonyModHooks, all patches, Config, UserConfig, VanishComponent, Disappear/Reappear
└── HarmonyMods.RustGame.Vanish.VanishExtensions/
    └── Methods.cs                    # IsOnline, Cast, Where, Select, ToList, Player (Option), DestroyUi, AddUi (no System.Linq)
```

**Config:** `HarmonyConfig/Vanish.json` (JSON). Icon: `HarmonyConfig/Vanish.png` or `Vanish.b64`. Config directory created in `Config` constructor if missing.

---

## Required Sections (IN THIS ORDER)

When creating Vanish documentation, include these sections. Adapt content to what exists in the analyzed code.

### 1) Mod Identity
- Mod name, author (nivex), purpose (admin vanish, visibility/network/AI suppression)
- Primary responsibilities (toggle vanish, command interception, config, permanent vs temporary patches)
- Key flags: `IsShuttingDown`, `config.CanSeeEveryone`, `player.limitNetworking` for "is vanished", `HasAccess` (authLevel / config)

### 2) Project Structure & Topology
- Single main assembly; entry: `Manager : IHarmonyModHooks` in `HarmonyMods.RustGame.Vanish`.
- Config path: `HarmonyConfig/Vanish.json`; icon `HarmonyConfig/Vanish.png` or `Vanish.b64`.
- State flow: Config load → `HiddenPlayers` (Dictionary by UserIDString → VanishComponent); `UserConfig.Get(ulong)` reads existing settings (or defaults); `GetOrCreate` saves a Users entry only for players with vanish access.

### 3) Persistent Data Model (CRITICAL)
- **HiddenPlayers:** `Dictionary<string, VanishComponent>` keyed by `BasePlayer.UserIDString`. Only contains currently vanished players. Cleared implicitly when each is destroyed on `Reappear`.
- **VanishComponent:** Per vanished player. Holds `player`, `child` GameObject, collider, `userid`, `workbenchCraft`, `lastPosition` (for CanSeeEveryone updates). Lifecycle: added in `Disappear` via `AddComponent<VanishComponent>()`, destroyed in `Reappear`. Awake: scale to zero, create child, start network group updates. OnDestroy: restore scale, stop updates, destroy child/collider.
- **Config:** `Config.Users` (ulong → UserConfig), `Config.AccessList` (ulong), `Config.CanSeeEveryone` (ulong), `Config.Messages` (string → string). Stored in `HarmonyConfig/Vanish.json`. `UserConfig` includes SafePoints (List&lt;Vector3&gt;), EffectsDisappear/EffectsAppear, image/UI fields, damage block, auto vanish, noclip, etc.

### 4) Configuration Schema
Document top-level and per-user fields:

| Level | Field | Type | Default / note |
|-------|--------|------|----------------|
| Config | Users | Dictionary&lt;ulong, UserConfig&gt; | Per-user settings, created only for authLevel / IsAdmin / AccessList |
| Config | CanSeeEveryone | List&lt;ulong&gt; | SteamIDs that always see vanished players; gates ShouldNetworkTo patch |
| Config | AccessList | List&lt;ulong&gt; | SteamIDs allowed to use vanish (with HasAccess) |
| Config | Messages | Dictionary&lt;string, string&gt; | Localized strings (Disabled, Enabled, Saved, etc.) |
| UserConfig | ShowIndicator, BlockAllIncomingDamage, BlockAllOutgoingDamage | bool | true, true, false |
| UserConfig | AutoVanish, NoClipOnConnect, NoClipOnUse, UseBags, UseSafePoints, SafePointsRemoval | bool | Various |
| UserConfig | EffectsDisappear, EffectsAppear | List&lt;string&gt; | Prefab paths |
| UserConfig | SafePoints | List&lt;Vector3&gt; | Stored as "x y z" in JSON (UnityVector3Converter) |
| UserConfig | ImageBase64, ImageColor, ImageOffsetMin/Max, ImageScaleFactor | string/float | UI indicator |

Config load: `Config.ReloadConfig()` (OnLoaded) prunes Users who are in the world without vanish access; save on UserConfig.GetOrCreate when a permitted user is new, and on various SetConfig commands.

### 5) Console / Chat Command Surface
Commands are intercepted via **ConsoleSystem.RunWithResult** prefix: only when `HasAccess(Connection)` (authLevel != 0) and command string contains "vanish" (after stripping chat prefixes and normalizing).

| Command / subcommand | Purpose | Side effects |
|----------------------|---------|--------------|
| `vanish` (no args) | Toggle vanish for the invoking player | ToggleVanish(player) |
| `vanish setanchormin x y` / `setanchormax` | Set UI image anchor offsets | UserConfig update, ShowUI refresh |
| `vanish anchors_save` / `anchors_reset` | Save or reset anchor values | Config save |
| `vanish reload` | Reload config from disk | Config.ReloadConfig() |
| `vanish safepoint` | Add current position to user SafePoints | UserConfig.SafePoints.Add, SaveConfig |
| `vanish showloot` | Open loot of entity/player in crosshair | VanishComponent.ShowLoot |
| `vanish access add\|remove &lt;name&gt;` | Grant/revoke AccessList for player | Config.AccessList, SaveConfig |
| `vanish resetimg` | Reset user icon image | LoadImage, SaveConfig |
| `vanish noclip` | Toggle noclip for player | SendConsoleCommand("noclip") |

Result: command is consumed (return false) so game does not process it again.

### 6) Harmony Patches & Event Flow (CRITICAL)
**Permanent patches** (always applied after validation):
- **ConsoleSystem.RunWithResult** — Prefix: intercept "vanish" commands, toggle or SetConfig; return false to suppress original.
- **ServerMgr.OnDisconnected** — Prefix: vanished players only; optional bag/safepoint/underground teleport. Does **not** disable colliders on admin logoff (that killed sleepers and wiped inventory).
- **BasePlayer.PlayerInit** — Postfix: apply auto-vanish / re-vanish for loaded players after save load or connect.
- **ServerMgr.Shutdown** — Prefix: set `IsShuttingDown = true`.
- **SaveRestore.Load** — Prefix: on load, re-vanish or auto-vanish players; optionally clear SafePoints (SafePointsRemoval).
- **SenseComponent.CanTarget** — Prefix: block targeting vanished players.
- **BaseNpc.GetWantsToAttack** — Prefix: block attack desire on vanished.
- **AIBrainSenses.GetNearest** — Prefix: exclude vanished from nearest-entity queries.
- **SimpleAIMemory.SetKnown** — Prefix: block adding vanished to AI memory.
- **BasePlayer.OcclusionPlayerFound** — Prefix: if observer is vanished (limitNetworking), do not report as found.

**Temporary patches** (only when `HiddenPlayers.Count > 0`; applied in TryPatchTemporary, removed in TryUnpatchTemporary when count goes to 0):
- **BasePlayer.ShouldNetworkTo** — Prefix: if target is in `CanSeeEveryone`, return true (so they see vanished). Skipped entirely if `CanSeeEveryone.Count == 0`.
- **BradleyAPC.VisibilityTest** — Prefix: exclude vanished from Bradley targeting.
- **BasePlayer.IsHostileItem** — Prefix: not hostile when vanished.
- **AntiHack.AddViolation** — Prefix: skip violations for vanished.
- **RelationshipManager.PlayerTeam.SendInvite** — Prefix: block team invite from vanished.
- **BasePlayer.EnablePlayerCollider** — Prefix: block enable while vanished.
- **BasePlayer.MarkHostileFor** — Prefix: block for vanished.
- **BasePlayer.Hurt** / **OnAttacked** / **Die** — Prefix: block damage/death when user config blocks incoming/outgoing.
- **BasePlayer.get_currentCraftLevel** — Prefix: fake workbench level when WorkbenchCraft.
- **PlayerLoot.StartLootingEntity**, **StorageContainer.CanBeLooted**, **BasePlayer.CanBeLooted**, **BuildingPrivlidge.CanAdministrate** — Prefix: first-priority bypass for admins who are currently vanished, so other loot blockers do not deny admin vanish inspection.
- **CodeLock.OnTryToOpen/OnTryToClose**, **KeyLock.OnTryToOpen/OnTryToClose** — Prefix: allow vanished to use locks.
- **Oxide.Core.Interface.CallHook** ("CanLootEntity", "IOnCupboardAuthorize", cupboard deauthorize/clear hooks) — Prefix (only if Oxide present): let admins who are currently vanished loot and manage cupboard authorization.

Patch application: `PatchAll()` uses `PatchDefinition` list; validates with `AccessTools.Method`; if validation fails and `CancelOnError`, load aborts. Temporary patches applied after permanent; unpatched when last vanished player reappears.

### 7) Lifecycle & State Machine
1. **OnLoaded** → `Instance = this`, `Config.ReloadConfig()`, `PatchAll()` (permanent + temporary if any already vanished), log. If save already loaded, `SaveRestore_Load.OnSaveRestoreLoad("")` to re-apply vanish state. **Icon:** Not loaded in OnLoaded; loaded lazily on first UI show (`ShowUIInternal` → `LoadVanishIconOnce`) so FileStorage is not touched before server identity is set (see HARMONY_MODS_GUIDE §12).
2. **OnUnloaded** → For each VanishComponent: if not shutting down and player exists and limitNetworking, `Reappear(player)`; then Destroy component. `UnpatchAll()`, `Reappear()` (global), `config = null`, `Instance = null`, log.
3. **Runtime** — Commands → ToggleVanish or SetConfig. Disappear: add/find VanishComponent, set limitNetworking, remove from subscribers (except CanSeeEveryone), disable collider, syncPosition false, apply UserConfig (UI, effects, noclip). Reappear: remove from HiddenPlayers, destroy component, restore collider/network/syncPosition, TryUnpatchTemporary if count 0.
4. **SaveRestore.Load** — After load, iterate BasePlayer.allPlayerList; disconnected + vanished → Disappear; disconnected + HasAccess + AutoVanish → Disappear; connected → BasePlayer_PlayerInit logic (re-vanish/auto-vanish). Optionally clear SafePoints if SafePointsRemoval.

**Invariants:** `IsVanished(player)` must match `HiddenPlayers` and `limitNetworking`. Temporary patches must be applied only when `HiddenPlayers.Count > 0`.

### 8) Oxide Integration (Optional)
- **Conditional patch:** `Type.GetType("Oxide.Core.Interface, Oxide.Core")`; if non-null, add temporary patches for `Interface.CallHook` overloads used by loot and cupboard authorization. Prefix: if the player is both admin and vanished, set `__result = null` and return false so they can loot or authorize.
- No Oxide reference in .csproj; runtime detection only.

### 9) VanishExtensions (Methods.cs)
- **IsOnline(BasePlayer)** — not null and Connection != null.
- **Cast&lt;T&gt;(entity, out T)** — safe cast to T for BaseNetworkable.
- **Where / Select / ToList** — LINQ-like without System.Linq (for environments that exclude it).
- **Player(Option)** — options.Connection?.player as BasePlayer.
- **DestroyUi(player, elem)** — CommunityEntity.ServerInstance.ClientRPC DestroyUI to player.
- **AddUi(player, json)** — ClientRPC AddUI with json (used for VanishGUI).

Use these when editing the mod to avoid introducing System.Linq or duplicate logic.

### 10) What NOT to Touch Without Care
- **Patch target method signatures** — Rust version–dependent; ValidatePatchDefinitions uses AccessTools.Method with explicit parameter types; missing method can cancel load (CancelOnError).
- **ShouldNetworkTo** — Only applied when `CanSeeEveryone.Count > 0`; changing this condition affects who can see vanished.
- **limitNetworking** — Game flag; changing semantics breaks IsVanished and visibility.
- **Config path and file names** — `HarmonyConfig/Vanish.json`, `Vanish.b64`, `Vanish.png`; and JSON keys (e.g. Vector3 format "x y z" via UnityVector3Converter).
- **VanishComponent lifecycle** — Must be added/removed only via Disappear/Reappear; OnDestroy restores scale and cleans invokes.
- **TryPatchTemporary / TryUnpatchTemporary** — Guard with `HiddenPlayers.Count` and (for unpatch) `NextTick`/invoke to avoid re-entrancy; temporary list must be unpatched when count reaches 0.
- **FileStorage** — Do not use in OnLoaded. Icon is lazy-loaded on first UI show (HARMONY_MODS_GUIDE.md Best Practices §12).

### 11) Performance Anti-Patterns
**Reference:** `.cursor/PluginInstructionalFiles/#System.Linq-Removal.md`, `Rust_Plugin_Performance_Best_Practices.md`.

**Entity Lookup Rules (CRITICAL):**
1. **NEVER iterate `BaseNetworkable.serverEntities`** for lookup — use `Find(NetworkableId)` when you have an ID.
2. **NEVER retry with expensive search** if `Find()` fails — set ID to 0 and move on.
3. **NEVER use reflection** for methods in the same mod — use internal/public and call directly.
4. **NEVER invalidate valid cached data** in loops — skip already-cached entities.
5. **Maintain spawn-time indexes** when searching by criteria other than NetworkableId.

**Vanish-specific:** HiddenPlayers is keyed by UserIDString; use it for lookups. SeeEveryoneUpdate iterates `Instance.HiddenPlayers.Values` and queues updates; keep that path minimal.

### 12) Comparison with Oxide Vanish (Whispers88)

**Reference:** `.cursor/Oxide.Plugins.Cant-Use/Vanish.cs` (Oxide plugin; not loadable in this workspace but useful for feature comparison).

| Feature | Oxide Vanish | Harmony Vanish |
|--------|---------------|----------------|
| **Access** | Permissions (vanish.allow, vanish.unlock, vanish.damage, etc.) | Config AccessList + authLevel; HasAccess(id) |
| **Persistence** | Data file `VanishPlayers` (_hiddenOffline); re-vanish on connect | SaveRestore.Load re-applies vanish for disconnected; no separate persist file |
| **UI icon** | CuiHelper; ImageUrlIcon (URL) + ImageSprite fallback; optional **NativeIcon** (debug.setinvis_ui) | FileStorage PNG, loaded on first UI show (not in OnLoaded); Hud.Menu, left of Backpacks; BuildVanishIndicatorJson. IconUrlConfig optional fallback. No native icon. |
| **Inventory view** | Commands `inv` / `invspy` (config); raycast or by name/ID; vanish.invviewer perm | `vanish showloot` (crosshair); developer-level loot when vanished (CanBeLooted patches) |
| **Loot / locks** | CanUseLockedEntity (vanish.unlock); optional hook | CodeLock/KeyLock patches; StorageContainer/BasePlayer CanBeLooted patches when vanished |
| **Damage** | OnEntityTakeDamage optional; vanish.damage allows damage from vanished | UserConfig BlockAllIncomingDamage / BlockAllOutgoingDamage; Hurt/OnAttacked/Die patches |
| **Metabolism** | Full pause (calories, hydration, temp, radiation, oxygen, wetness) + RestartMetabolism on reappear | Config **Pause metabolism when vanished**; MetabolismPause/RestartMetabolism in Disappear/Reappear |
| **Noclip** | NoClipOnVanish (runs noclip on vanish); optional on reappear if flying | UserConfig NoClipOnUse; SendConsoleCommand("noclip") |
| **Sounds** | EnableSound, PublicSound (world vs player), configurable effect paths | UserConfig SoundEffects, EffectsDisappear/EffectsAppear (prefab list) |
| **Keybind** | Command args True/False to force vanish/reappear | First-arg force: `vanish on` / `vanish off` (and true/false/1/0) for keybinds (e.g. `bind 8 vanish;noclip`) |
| **Teleport to marker** | OnMapMarkerAdd; vanished + reload on map marker → teleport to marker position (vanish.teleport) | Config **Teleport to map marker when vanished**; BasePlayer.Server_AddMarker prefix/postfix teleports vanished player to new marker |
| **Connect/disconnect** | EnforceOnConnect, EnforceOnDisconnect; HideOnDisconnect; UnderWorldOnDisconnect / AboveWorldOnConnect (teleport below/above terrain) | SaveRestore + PlayerInit re-vanish; SafePoints for position; no underworld/aboveworld teleport |
| **Native invis icon** | NativeIcon → debug.setinvis_ui true/false (game built-in) | Not wanted; we keep custom icon only. |
| **Redirect** | debug.invis / invis → /vanish | Not implemented. **Meaning:** when a player runs the game's built-in `invis` command, run our vanish logic instead (optional parity). |
| **Spectate** | OnPlayerSpectate/End; destroy/recreate VanishPositionUpdate | Not documented in lifecycle |
| **Workbench** | VanishPositionUpdate OnTriggerEnter/Exit TriggerWorkbench; fake craft level | WorkbenchCraft + get_currentCraftLevel patch |

**Improvements to consider porting to Harmony:**
- **Native icon option** — Not desired; we keep our custom icon only (no `debug.setinvis_ui` config).
- **IconUrlConfig** — Optional fallback only; we prefer local FileStorage for speed; use URL in indicator only when needed.
- **Metabolism pause** — Implemented (config "Pause metabolism when vanished"; MetabolismPause/RestartMetabolism).
- **Teleport to map marker** — Implemented (config "Teleport to map marker when vanished"; Server_AddMarker patch).
- **Keybind support** — Implemented (`vanish on` / `vanish off` and true/false/1/0).
- **AboveWorldOnConnect / UnderWorldOnDisconnect** — Optional teleport vanished players above/below terrain on connect/disconnect to avoid stuck state.
- **Redirect debug.invis** — When a player runs the game's built-in `invis` command, run our vanish logic instead (optional).

---

## Output Rules (STRICT)
- Output **Markdown only**.
- **File name format:** `Vanish_Dictionary.md` or `Vanish_Instructional.md` (for the resulting doc); this file is `Vanish_Markdown_Creation_Instructional.md` (the template for creating that doc).
- **No code dumps** (no full method bodies). Tiny excerpts only when essential.
- Prefer **tables, bullet lists, concise descriptions**.
- Document **what Vanish does and how it is wired**, not generic Harmony/Rust concepts.

---

## Exclusions
- Full method listings
- Generic Harmony/Rust/Unity tutorials
- Oxide lifecycle (Vanish only optionally patches Oxide.CallHook)

---

## Final Check
Before outputting a Vanish dictionary/instructional, verify:
- Document would **materially improve** an AI's ability to change Vanish safely.
- Permanent vs temporary patches are clearly listed and when each is applied.
- Config, UserConfig, and Messages are accurate; CanSeeEveryone and AccessList behavior is clear.
- Lifecycle (OnLoaded, OnUnloaded, Disappear, Reappear, TryPatchTemporary, TryUnpatchTemporary) is correct.
- Performance and "do not touch" sections are included.
- When adding features or improving the mod, consult **section 12) Comparison with Oxide Vanish** (`.cursor/Oxide.Plugins.Cant-Use/Vanish.cs`) for the improvement checklist.
