# RemoverTool (Harmony port of Oxide RemoverTool 4.3.431)

Harmony-first, Oxide-free port of the Oxide plugin **Remover Tool** (Reneb/Fuji/Arainrr/Tryhard).
Behavioural replica of the original 4.3.431 logic running as a Rust Harmony mod.

## Load order

```
0Permissions  ->  (optional) Economics, RustRewards  ->  RemoverTool
```

- `0Permissions.dll` provides the permission backend (see Framework §10a). RemoverTool links to it
  lazily and re-registers permissions when 0Permissions loads/reloads.
- `Economics` / `RustRewards` are optional. When present they are used for currency pay/refund;
  when absent those currency modes decline gracefully.

## Paths

- Config: `HarmonyConfig/RemoverTool.json`
- Data:   `HarmonyData/RemoverTool/` (logs in `HarmonyData/RemoverTool/logs/`)
- Lang:   `HarmonyLanguage/RemoverTool.json` (optional; merged over the built-in English messages)

## Chat routing

`/remove` is registered on the shared `ChatSayBridge` (same AppDomain dispatcher as BetterChat, Shop, SkillTree). Vanilla `Chat.sayAs` silently drops slash commands, and HarmonyX skips remaining `Chat.say` prefixes when one returns false — so a later `harmony.reload` of another chat-prefix mod (for example DynamicCupShare) used to leave `/remove` with no handler and no chat output. Reload of RemoverTool looked like a fix because it put this mod's prefix first again.

## Commands

- Chat: configurable via `Chat Settings > Command` (default `/remove`)
- Console:
  - `remove.toggle` — toggle the remover tool for the calling player
  - `remove.target <normal|admin|all|structure|external|disable> <player> [time] [max]`
  - `remove.building <price|refund|priceP|refundP> <percentage>`
  - `remove.allow <true|false>` — enable/disable removing globally
  - `remove.playerentity <all|cupboard|building> <player>`

## Permissions

- `removertool.all`
- `removertool.admin`
- `removertool.normal`
- `removertool.target`
- `removertool.external`
- `removertool.override`
- `removertool.structure`
- plus any custom permission keys defined in the config's `Permission Settings` block.

## Optional plugin integrations

These optional Oxide plugins are resolved lazily. Ones without a Harmony port resolve to `null`
so the plugin falls back gracefully (matching Oxide's `PluginReference` behaviour):

| Reference           | Status                                             |
|---------------------|----------------------------------------------------|
| Economics           | Bridged to the Economics Harmony mod if loaded     |
| ServerRewards       | Bridged to the RustRewards Harmony wrapper if loaded|
| Friends             | Not ported — resolves to null (stub)               |
| Clans               | Not ported — resolves to null (stub)               |
| ImageLibrary        | Not ported — resolves to null (crosshair/entity images skipped) |
| BuildingOwners      | Not ported — resolves to null (stub)               |
| RustTranslationAPI  | Not ported — resolves to null (English display names) |
| NoEscape            | Not ported — raid/combat block checks resolve to null |

## Build

```powershell
.\build.ps1
```

Builds Release and copies only `RemoverTool.dll` to `<server root>\HarmonyMods\RemoverTool.dll`.
No dependency DLLs are copied.
