# InventoryShortcuts

Adds **Quests** and **Skills** buttons at the top of the inventory panel, and **Outpost**, **Players**, **Kits**, **Shop**, **Skins** buttons at the bottom (under the hotbar). When any button is clicked, loot/inventory is closed before the target UI opens.

## How it works (no Oxide required)

The UI uses parent **`Inventory`** – a game UI panel layer. When you attach CUI to it, the buttons show when the Tab inventory is open and hide when it closes. **No server-side hook needed.** Create once on spawn (EndSleeping), leave up; the game handles show/hide.

Triggers for sending the UI (first-time only):
- **BasePlayer.EndSleeping** – on spawn (with 0.2s delay so client CUI is ready)
- **PlayerLoot.AddContainer** – fallback when opening loot (crate, TC, corpse, backpack)

## Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | InventoryShortcuts |
| **Components** | Harmony mod only (uses Inventory UI layer) |
| **Purpose** | Add shortcut buttons at top and bottom of inventory |
| **Dependencies** | XDQuest, SkillTree, Outpost, TP, Kits, Shop, Skinshop (or equivalents) |

## Features

**Top row (Quests, Skills):**
| Button | Command | Opens |
|--------|---------|-------|
| **QUESTS** | `/quest` | XDQuest quest list panel |
| **SKILLS** | `/st` | SkillTree skill tree panel |

**Bottom row (under hotbar slot 1, no Bandit):**
| Button | Command | Opens |
|--------|---------|-------|
| **OUTPOST** | `/outpost` | Outpost UI |
| **PLAYERS** | `/tp` | Players/TP list |
| **KITS** | `/kits` | Kits menu |
| **SHOP** | `/shop` | Shop UI |
| **SKINS** | `/skinshop` | Skinshop UI |

When any button is clicked, loot/inventory is closed (via `EndLooting`) so the target UI can open cleanly.

**Admin:** `/gridlines` — Toggles an in-game **percentage grid overlay** (0–1) on your screen so you can read exact normalized positions for UI layout. Grid stays until you click the **×** (top-right). Admin only.

## Project Structure

| File | Responsibility |
|------|----------------|
| `InventoryShortcutsMod.cs` | Lifecycle, CUI buttons, ShowButtons, DestroyUi, grid overlay |
| `InventoryShortcutsConfig.cs` | Config load/save from HarmonyConfig |
| `UIGridOverlay.cs` | Builds CUI for in-game percentage grid (0–1); used by /gridlines |
| `Patches/Cui_Endtest_Patch.cs` | Handle button clicks via cui.endtest INVSHORTCUTS; GRIDCLOSE to close grid |
| `Patches/Chat_Say_Patch.cs` | /gridlines (admin only) – show grid overlay |
| `Patches/PlayerLoot_AddContainer_Patch.cs` | Show when player opens loot (crate, TC, backpack) |
| `Patches/BasePlayer_EndSleeping_Patch.cs` | Send UI on spawn (parent Inventory) |
| `Patches/BasePlayer_OnDisconnected_Patch.cs` | Cleanup on disconnect |

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Cui_Endtest_Patch` | `cui.endtest` | Prefix | Handle INVSHORTCUTS (QUEST/SKILLS/OUTPOST/PLAYERS/KITS/SHOP/SKINS); run chat.say, close loot |
| `PlayerLoot_AddContainer_Patch` | `PlayerLoot.AddContainer` | Postfix | Show when player opens loot |
| `BasePlayer_EndSleeping_Patch` | `BasePlayer.EndSleeping` | Postfix | Send UI once on spawn (0.2s delay) |
| `BasePlayer_OnDisconnected_Patch` | `BasePlayer.OnDisconnected` | Postfix | Destroy UI, clear state on disconnect |
| `Chat_Say_Patch` | `ConVar.Chat.say` | Prefix | Handle /gridlines (admin only); show grid overlay |

## UI (CUI)

- **Parent:** `Inventory` (default, shows when Tab open), or `Hud`, `Overlay`. Use `Hud` in config if "Unknown Parent" appears.
- **Position:** All placements use **normalized coordinates (0–1)** — anchors only, no pixel offsets — so the UI scales correctly on any resolution.
- **Style:** Dark gray background, white text/icons to match game tab bar.
- **Icons:** Text only by default. Set `QuestIconShortname` / `SkillIconShortname` for native item icons (`itemid`, no server file RPC). Optional `SkillIconImageUrl` uses client-side `url` (like Oxide CUI) — **not** FileStorage `png` (avoids `CL_ReceiveFilePng` / gameplay RPC paths).
- **RPC:** `CuiHelper` sends only `AddUI` / `DestroyUI` with JSON strings (same as Oxide `RustCui` / Vanish `AddUi`). No inventory hooks, no FileStorage, no `CL_ReceiveFilePng`.
- **Commands:** `cui.endtest INVSHORTCUTS QUEST`, `SKILLS`, `OUTPOST`, `PLAYERS`, `KITS`, `SHOP`, `SKINS` (forwarded to server).

## Config

| Path | `HarmonyConfig/InventoryShortcuts.json` |
|------|----------------------------------------|
| **CuiParent** | `Inventory`, `Hud`, or `Overlay` |
| **ButtonColor** | RGBA for button background (default dark gray) |
| **TextColor** | RGBA for text/icons (default white) |
| **AnchorTop** | Button row top anchor (0–1) |
| **ButtonHeight** | Height of top button row (0–1) |
| **ButtonWidth** | Width of each top button (0–1, fraction of screen) |
| **LeftButtonCenter** / **RightButtonCenter** | Center X of Quests/Skills (0–1) |
| **QuestButtonShiftX** / **SkillButtonShiftX** | Fine-tune position (normalized; negative = left, positive = right; ~0.005 ≈ 14px at 2560 width) |
| **ExtraButtonHeight** | Extra height for top row (0–1, fraction of screen) |
| **QuestIconShortname** | Item for quest icon (e.g. `note`, `paper`; empty = text only) |
| **SkillIconShortname** | Item for skill icon (empty = text only) |
| **HotbarButtonHeight** | Height of bottom button row (0–1) |
| **Debug** | Verbose logging |

All UI positions use **percentages (normalized 0–1)** so alignment is consistent across resolutions.

## Lifecycle

- **OnLoaded:** Load config, set Instance.
- **OnUnloaded:** Destroy UI for all players, clear Instance.
- **ShowButtons:** Called from AddContainer patch when player opens loot.
- **DestroyUi:** Called on unload.

## Custom Icons

Use `QuestIconShortname` / `SkillIconShortname` for built-in item icons, or `SkillIconImageUrl` for a hosted HTTPS image (`url` in CUI — client fetches; no server png RPC). Do **not** use FileStorage `png` IDs in this mod; that path triggers `CL_ReceiveFilePng` and can break across Rust updates.

## Build & Deploy

1. **Harmony mod:** `cd .cursor/HarmonyMods/InventoryShortcuts && ./build.ps1` → copies to `HarmonyMods/InventoryShortcuts.dll`
2. Load: `harmony.load InventoryShortcuts`
