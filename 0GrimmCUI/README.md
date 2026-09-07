# 0GrimmCUI — Shared CUI Foundation

Loads first (alphabetically: `0Permissions` → **`0GrimmCUI`** → `0GrimmNPC` → feature mods).

## What it provides

| Component | Purpose |
|-----------|---------|
| `Game.Rust.Cui` | Full Harmony-compatible `CuiHelper`, components, JSON serialization |
| `Ext.Chaos.UIFramework` | Declarative Chaos UI builder (layouts, pooling, callbacks) — shared by AdminMenu, Minimap, StackManager, etc. |
| `GrimmCui` router | Single `cui.endtest` + `Hook_DragRPC` Harmony patch |
| `GrimmCuiPatterns` | Common registration helpers (command bridge, Chaos callback bridge) |
| Ready callbacks | `GrimmCui.RegisterReadyCallback` — survives reload / load order |

## Architecture

- **RustCui** — low-level JSON/CUI types used by all mods (Kits, RaidableBases, Trade, …).
- **Chaos UIFramework** — higher-level declarative UI that **generates** RustCui JSON internally. Mods using Chaos exclude local `UIFramework/`, `Pooling/`, and `Json/` folders and reference this DLL instead.
- **GrimmCui router** — bridges client `cui.endtest MARKER …` payloads back to server handlers; applies per-mod JSON rewriters before `AddUI`.

Plain RustCui mods do **not** need Chaos. Chaos mods do **not** need a local copy of the framework.

## Consumer mod integration

1. Reference `HarmonyMods/0GrimmCUI.dll` in your `.csproj`
2. Exclude local `RustCui.cs`, `UIFramework/`, `Pooling/`, `Json/`, and `Patches/Cui_*` from compile
3. Add `GrimmCuiRegistration.cs`:

```csharp
using GrimmCuiHarmony;

internal static class GrimmCuiRegistration
{
    internal static void Register()
    {
        GrimmCui.RegisterEndtest("MYMARKER", (_, src) => MyMod.Instance?.HandleCuiEndtest(src));
        GrimmCui.RegisterJsonRewriter(json => json.Replace(
            "\"command\":\"my.cmd", "\"command\":\"cui.endtest MYMARKER my.cmd"));
    }
}
```

**Chaos UI mods** also register a callback rewriter:

```csharp
GrimmCui.RegisterJsonRewriter(
    GrimmCuiPatterns.CreateChaosCallbackRewriter("ADMINMENU", "adminmenu.callback"));
```

**DynamicCupShare-style** mods embed `cui.endtest` directly via `new CommandCallbackHandler(this, "DYNAMICCUPSHARE")` — no callback rewriter needed.

4. In `OnLoaded`:

```csharp
GrimmCui.RegisterReadyCallback(GrimmCuiRegistration.Register);
```

### Drag support

```csharp
GrimmCui.RegisterDragPrefix("RB_UI_", (player, name, pos, type) =>
    RaidableBasesHost.Instance?.ModInstance?.DispatchOnCuiDraggableDrag(player, name, pos, type));
```

## Build

```powershell
.\build.ps1
```

Output: `HarmonyMods/0GrimmCUI.dll`

## Reload order

After `harmony.reload 0GrimmCUI`, endtest/drag/rewriter registrations are cleared and all ready callbacks re-run. Handler maps are stored in **AppDomain** (not static fields on the GrimmCUI type), so consumer `Register*` calls from older Cecil-renamed GrimmCUI assemblies still populate the live router — clicks keep working without requiring every consumer mod reload.

Still preferred after a GrimmCUI *binary* update: reload important consumers (or restart) so they bind `CuiHelper` / Chaos types to the new assembly.

## Chaos consumer mods (use shared UIFramework)

| Mod | Marker | Callback prefix |
|-----|--------|-----------------|
| AdminMenu | `ADMINMENU` | `adminmenu.callback` |
| Minimap | `MINIMAP` | `minimap.callback` |
| StackManager | `STACKMANAGER` | `stackmanager.callback`, `stacksextended.callback` |
| AutoCodeLock | `AUTOCODELOCK` | `autocodelock.callback` |
| DynamicCupShare | `DYNAMICCUPSHARE` | embedded via `CommandCallbackHandler` marker |
| PlayerSkins | `PLAYERSKINS` | `playerskins.callback` |
| TeleportGUI | `TELEPORTGUI` | `teleportgui.callback` (Chaos UI restored via shared GrimmCUI) |
