# Backpacks Harmony Mod (Oxide-parity port)

Full port of **WhiteThunder Backpacks** (Oxide `CovalencePlugin`) to a standalone Harmony mod, with **3.17.6 game-update fixes** (loot `entitySource`, arctic spawn position, `decay = null`, network subscribe) plus Harmony CUI/`cui.endtest` bridging. Hosting uses Harmony shims instead of Oxide/Carbon.

## Build / load

```powershell
cd .cursor\HarmonyMods\Backpacks
.\build.ps1
```

Output: `HarmonyMods/Backpacks.dll` (server root).

```
harmony.load Backpacks
```

Requires **0Permissions.dll** loaded first (permission bridge via `Permissions_ApiType`).

## Config

- Path: `HarmonyConfig/Backpacks.json`
- Deserializes the Oxide `Configuration` class unchanged (`JsonProperty` names).
- Existing server config already sets the custom data directory (see below).

## Data (player backpacks)

Config key:

```json
"Custom backpack data directory (absolute path, empty = default oxide/data)": "C:\\!DataPersistence\\oxide\\data\\Backpacks"
```

Oxide custom-directory layout is preserved exactly:

| File | Path |
|------|------|
| Player backpacks | `<custom root>\<steamid>.json` |
| Preferences | `<custom root>\Backpacks.json` |
| Capacity | `<custom root>\BackpacksCapacity.json` |

JSON format is Oxide-compatible (`Items[]`, `OwnerID`, `GatherMode`, etc.). Do **not** invent a Page0/Page1 layout.

If the custom path is empty, default is `HarmonyData/Backpacks/` via `Interface.Oxide.DataFileSystem`.

## Permissions (registered on Init)

| Permission | Purpose |
|------------|---------|
| `backpacks.use` | Open backpack |
| `backpacks.gui` | GUI button |
| `backpacks.fetch` | Fetch items |
| `backpacks.gather` | Gather mode |
| `backpacks.retrieve` | Retrieve mode (needs ItemRetriever) |
| `backpacks.keepondeath` | Keep on death |
| `backpacks.nofoodspoiling` | Food spoiling exemption |
| `backpacks.admin` / `.view` / `.edit` / `.resize` / `.debug` / `.protected` | Admin |
| `backpacks.size.*` / profile perms | Size / capacity |

Bridged to `PermissionsHarmony.PermissionsMod` (`UserHasPermission`, `UserHasGroup`, `RegisterPermission`).

## Commands

Chat / console (IPlayer covalence style):

- `backpack`, `backpack.open`, `backpack.next`, `backpack.prev` / `backpack.previous`
- `backpack.fetch`, `backpack.erase`, `viewbackpack`
- `backpack.addsize`, `backpack.setsize`, `backpack.resetgui`, `backpackgui`
- `backpack.setgathermode`, `backpack.ui.togglegather`, `backpack.ui.toggleretrieve`
- `backpack.debug.size` / `backpack.debug.capacity`, `backpack.debug.gather`

## Deferred / gaps

- **ItemRetriever** — Harmony mod (`HarmonyMods/ItemRetriever.dll`). Backpacks binds via AppDomain ready callback — **no load-order requirement**. Auto-start is typically alphabetical (`Backpacks` then `ItemRetriever` then `Permissions`); that is fine.
- **OnNetworkSubscriptionsUpdate** — not patched (no clean Harmony target yet).
- **Arena / EventManager / BackpackButton** — PluginReferences stubbed null (same as Oxide when those plugins are absent).
- **OnGroupPermission / OnUserPermission** hooks — not patched yet (capacity refresh on grant/revoke may need a Permissions event bridge later). GUI buttons are refreshed on Permissions ready/rebind so file merges / `harmony.load 0Permissions` update online players.
- **CUI buttons** — rewritten to `cui.endtest BP …` (clients only forward ConsoleGen commands).

## Startup order (auto)

Facepunch HarmonyLoader enumerates `HarmonyMods/*.dll` (filesystem order; often alphabetical). Do **not** rely on:

```
Permissions -> ItemRetriever -> Backpacks
```

That order only applies to manual `harmony.load`. On boot you get roughly `Backpacks` -> `ItemRetriever` -> `Permissions`. Both **Permissions** and **ItemRetriever** publish ready callbacks so Backpacks rebinds when they load later.

## Source layout

```
.cursor/HarmonyMods/Backpacks/
  Backpacks.cs              # converted from Oxide.Plugins.Cant-Use/Backpacks.cs
  BackpacksCompat.cs        # Oxide API shims
  BackpacksHarmonyMod.cs    # IHarmonyModHooks entry
  RustCui.cs                # Oxide.Game.Rust.Cui
  convert-from-oxide.ps1
  Backpacks.csproj
  build.ps1
  Patches/
  _minimal_archive/         # previous minimal mod (archived)
```

Regenerate plugin body after Oxide source edits:

```powershell
.\convert-from-oxide.ps1
.\build.ps1
```
