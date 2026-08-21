# CopyPaste (Harmony Mod)

**No Oxide dependency.** Harmony port of Oxide **CopyPaste 4.2.81** — same copy/paste logic, adapted only for Harmony hosting (config/data paths, chat commands, timers, RaidableBases API). Local fixes: DLC detection no longer uses `ItemBlueprint.NeedsSteamDLC` (boot NRE), and paste applies default-skin wallpaper when `wallpaperHealth > 0`.

## Mod identity

| Field | Value |
|-------|--------|
| **Name** | CopyPaste |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Oxide** | None — for Oxide-free servers only |
| **API version** | **4.2.81** (`VersionNumber` for RaidableBases; requires ≥ 4.2.7) |
| **Config** | `HarmonyConfig/CopyPaste.json` |
| **Data** | `HarmonyData/copypaste/*.json` |

## Project structure

| File | Content |
|------|--------|
| `CopyPaste.cs` | Full 4.2.81 plugin logic (ported from Oxide; near-verbatim) |
| `CopyPasteCompat.cs` | Oxide shims: `IPlayer`, data/config files, timers, lang, permissions |
| `CopyPasteHarmonyMod.cs` | Harmony entry, AppDomain API handshake, static API, command registration |
| `Patches/Chat_Say_Patch.cs` | Prefix on `ConVar.Chat.say` for `/copy`, `/paste`, etc. |
| `convert-from-oxide.ps1` | Regenerates `CopyPaste.cs` from `Oxide.Plugins.Cant-Use/CopyPaste4.2.81.cs` |
| `CopyPaste.csproj` | Game refs + **Krafs.Publicizer** (private game fields, same as RaidableBases) |
| `build.ps1` | Build and copy DLL to `HarmonyMods/` |

## What stays different from Oxide (by design)

| Oxide | Harmony |
|-------|---------|
| `oxide/data/copypaste/` | `HarmonyData/copypaste/` |
| `oxide/config/CopyPaste.json` | `HarmonyConfig/CopyPaste.json` |
| Covalence `[Command]` + permissions | ConsoleSystem + `/` chat patch; **admins** (or granted perms via host) |
| `Interface.CallHook` | No-op (no Oxide plugins) |
| Plugin discovery | `AppDomain` key `CopyPaste_ApiType` → `CopyPasteHarmony.CopyPasteHarmonyMod` |

Paste JSON format and entity handling match Oxide 4.2.81 (IO, inventories, signs, boats, farming, elevators, trackers, etc.).

## API for RaidableBases and other mods (no Oxide)

Target type: **`CopyPasteHarmony.CopyPasteHarmonyMod`** (static methods).

| Method / member | Description |
|-----------------|-------------|
| **PreLoadData** | Same as Oxide; returns **`List<>`** (order-preserving wrapper over Oxide `HashSet`) |
| **Paste** | Starts paste; returns `PasteData`. `player` may be `BasePlayer`, Harmony `IPlayer`, or RaidableBases console player |
| **FindBestHeight** | Best foundation ground Y (**+1f**, same as Oxide) |
| **Version** | `VersionNumber(4, 2, 81)` |
| **IsPasteReady** | True when item/skin definitions are ready |
| **PasteFromDataFile** | Optional: paste game `.data` via `ConVar.CopyPaste` |

## Chat commands

| Command | Description |
|---------|-------------|
| **/copy** | Copy looked-at structure → `HarmonyData/copypaste/<name>.json` |
| **/paste** | Paste at look hit (Oxide args: `height`, `autoheight`, `auth`, `stability`, …) |
| **/copylist** | List saved filenames |
| **/pasteback** | Paste at original saved position (**autoheight forced false**) |
| **/undo** | Undo last paste (batched) |

Access: admin **or** Oxide-style permission names (`copypaste.copy`, etc.) if granted via the host permission helper. By default only admins pass `HasAccess`.

## Build and deploy

```powershell
.\.cursor\HarmonyMods\CopyPaste\build.ps1
```

DLL → **`HarmonyMods/CopyPaste.dll`**. Load: `harmony.load CopyPaste`.

To refresh from a newer Oxide source:

```powershell
.\.cursor\HarmonyMods\CopyPaste\convert-from-oxide.ps1
# then build again
```

## Reference

- **Oxide source of truth:** `.cursor/Oxide.Plugins.Cant-Use/CopyPaste4.2.81.cs`
- **Harmony mod guide:** `.cursor/!Harmony-Assembly/HARMONY_MODS_GUIDE.md`
- **RaidableBases:** `.cursor/HarmonyMods/RaidableBases/README.md`
