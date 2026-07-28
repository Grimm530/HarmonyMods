# PersonalNPC (Harmony)

Three Oxide plugins merged into a single Harmony mod, with no Oxide runtime:

| Oxide plugin | Version | Class in this mod |
| --- | --- | --- |
| `PersonalNPC.cs` | 2.0.7 | `PersonalNPCHarmony.PersonalNPC` |
| `PersonalNPCHelper.cs` | 1.3.0 | `PersonalNPCHarmony.PersonalNPCHelper` |
| `PersonalNPCAddonBuilder.cs` | 1.0.0 | `PersonalNPCHarmony.PNPCAddonBuilder` |

All three live in one assembly (`PersonalNPC.dll`, namespace `PersonalNPCHarmony`) and are created
and wired together by `PersonalNPCHarmonyMod`, which implements `IHarmonyModHooks`.

## Build and deploy

```powershell
.\.cursor\HarmonyMods\PersonalNPC\build.ps1
```

The script builds Release and copies `PersonalNPC.dll` into the server's `HarmonyMods/` folder.

## Load order

```
harmony.load 0Permissions
harmony.load PersonalNPC
```

`0Permissions` should be loaded first so permission grants persist. If it is missing, PersonalNPC
still loads: `PermissionsBridge` falls back to an in-memory permission store, logs a warning, and
late-binds to `0Permissions` automatically when it appears (it re-links on permission generation
changes, so grants made before the rebind are not lost).

## Commands

### Chat

| Command | Handled by | Notes |
| --- | --- | --- |
| `/pnpc ...` | PersonalNPC | Spawn / despawn / configure the personal bot. The helper's unlock gate runs first. |
| `/bw`, `/botwheel` | PersonalNPCHelper | Opens the bot task wheel. |

### Console

| Command | Handled by | Access |
| --- | --- | --- |
| `pnpc` | PersonalNPC | player |
| `pnpc.info` | PersonalNPC | player |
| `pnpc.deposit` | PersonalNPC | player |
| `pnpc.item` | PersonalNPC | server / admin |
| `pnpchelper.wheel` | PersonalNPCHelper | player |
| `pnpchelper.build` | PersonalNPCHelper | player |
| `pnpchelper.reset` | PersonalNPCHelper | server / admin |
| `pnpchelper.grant` | PersonalNPCHelper | server / admin |

## Permissions

Registered through `HarmonyPermissionHelper`:

- `personalnpc.bot1` (and one permission per entry in the config's bot permission map)
- `personalnpc.nocooldown`

The helper grants `personalnpc.bot1` when a player unlocks the bot, and revokes it from everyone on
wipe.

## Files and paths

| What | Path |
| --- | --- |
| Merged config | `HarmonyConfig/PersonalNPC/PersonalNPC.json` |
| Config fallback | `HarmonyConfig/PersonalNPC.json` (flat layout) |
| Builder config (legacy backup) | `HarmonyConfig/PersonalNPC/PNPCAddonBuilder.json` |
| Data root | `HarmonyData/` |
| Bot inventories | `HarmonyData/PersonalNPC/Inventories/BotInventories.json` |
| Helper unlock data | `HarmonyData/PersonalNPC/PersonalNPCHelper.json` |
| CopyPaste files (builder) | `HarmonyData/copypaste/` |
| Downloaded UI images | `HarmonyImages/PersonalNPC/` |

### One config file

There is a single config file. The builder addon's section is stored in the same
`PersonalNPC.json` under the original key `"Available buildings (by PNPC bot spawn name)"`.
On first load the host copies that key out of `PNPCAddonBuilder.json` if it is not present yet;
after that `PNPCAddonBuilder.json` is unused and kept only as a backup.

## CUI note

Rust clients only forward commands that exist in `ConsoleGen`, so the Oxide-style `pnpc` and
`pnpchelper.*` commands on CUI buttons never reach the server. `RustCui.cs` rewrites button
callbacks on the way out:

```
"command":"pnpchelper.  ->  "command":"cui.endtest PNPCHELPER pnpchelper.
"command":"pnpc         ->  "command":"cui.endtest PNPC pnpc
```

`Patches/Cui_Endtest_Patch.cs` unwraps the marker and dispatches to the matching handler. Any
`cui.endtest` payload without a `PNPC` / `PNPCHELPER` marker falls through untouched, so other
Harmony mods that use the same trick keep working.

## Hook coverage

Oxide hooks are replaced by Harmony patches under `Patches/`:

- `PlayerLifecycle_Patches.cs` - `OnPlayerConnected`, `OnPlayerDisconnected`, `OnPlayerRespawned`,
  `OnPlayerDeath`
- `Chat_Say_Patch.cs` - `/pnpc`, `/bw`, `/botwheel` plus the helper's `OnPlayerCommand` unlock gate
- `Cui_Endtest_Patch.cs` - CUI button routing
- `GameHooks_Patches.cs` - `OnEntityTakeDamage` (core + builder), `CanBeTargeted` (auto turrets and
  Gen2 NPC senses), `CanBradleyApcTarget`, `OnLoseCondition`, `OnItemAction`, `CanAcceptItem`,
  `CanMoveItem`, `OnDispenserGather`, `OnEntityBuilt`, `OnStructureUpgrade`, `OnEntitySpawned` /
  `OnEntityKill` for collectibles, `OnEntityMounted`, `CanUseGesture`, `CanUseLockedEntity`,
  `CanLootEntity` / `OnLootEntity`, `OnCorpsePopulate`, `OnServerSave`, `OnNewSave`

## Regenerating the ported sources

`convert-from-oxide.ps1` rebuilds `PersonalNPC.cs`, `PersonalNPCHelper.cs` and `PNPCAddonBuilder.cs`
from `.cursor/Oxide.Plugins.Cant-Use/`. It strips the Oxide usings and attributes, remaps
`Oxide.Plugins` to `PersonalNPCHarmony`, swaps `RustPlugin` for `PersonalNPCPluginBase`, adds the
`HarmonyInit` / `HarmonyServerInitialized` / `HarmonyUnload` entry points, and rewrites the data
paths. Run it, then run `build.ps1`.

## Known limitations

- **Soft dependencies are stubs.** `Friends`, `Clans`, `ZoneManager`, `DeployableNature`,
  `VehicleDeployedLocks`, `PNPCAddonHeli`, `PNPCAddonHunter` and `RaidableBasesBuyableUI` resolve to
  `null`, which is the same path the Oxide plugin takes when they are not installed. Features that
  depend on them (friend/clan sharing checks, zone rules, heli and hunter addons, the buyable-base
  UI) are inactive.
- **ImageLibrary is a built-in replacement, not the real plugin.** It downloads PNGs over HTTP into
  `HarmonyImages/PersonalNPC/`, caches them on disk and registers them with `FileStorage`. Images
  appear a moment after first load because the store is flushed on the main thread every 2 seconds.
  Without outbound network access, icons stay blank but nothing else breaks.
- **`OnDispenserBonus` is folded into `OnDispenserGather`.** Both Oxide hooks end at
  `BasePlayer.GiveItem` with the `ResourceHarvested` reason, and the plugin's two handlers are
  mutually exclusive by config, so only the gather path is patched.
- **`CanMoveItem` is approximated.** Oxide injects that hook inside the body of
  `PlayerInventory.MoveItem`, which is not reachable without an IL transpiler, so the patch sits on
  `Item.MoveToContainer` and reconstructs the arguments. Moves with no source player are ignored,
  matching the plugin's own null check.
- **`OnNewSave` relies on `SaveRestore.Load` returning false.** That is how a wipe is detected. If
  the mod is loaded after the save is restored, the wipe reset has to be done manually with
  `pnpchelper.reset`.
- Use **`harmony.load PersonalNPC`** after rebuild (loader unloads the previous copy first).
