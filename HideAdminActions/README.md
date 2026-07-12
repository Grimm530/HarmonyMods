# HideAdminActions Harmony Mod

Harmony mod that hides admin-specific chat indicators from players. Suppresses all F1 item-giving broadcast messages (e.g. "gave themselves", "gave X to Y", "gave everyone") and normalizes chat name colors so admin actions are less visible. Replaces [NoGiveNotices](https://umod.org/plugins/no-give-notices) functionality; inspired by [NoGreen](https://umod.org/plugins/no-green) for name color hiding.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Mod name** | HideAdminActions |
| **Type** | Harmony mod |
| **Purpose** | Hide admin chat broadcasts and name color indicators from player view |

**Primary responsibilities:**
- Suppress server broadcasts when the message is any admin "gave" notification (gave themselves, gave X to Y, gave everyone, blueprints, etc.) – full [NoGiveNotices](https://umod.org/plugins/no-give-notices) parity
- Override chat name color so all names appear in the same color (cyan `#5af`), hiding green admin text

**Key feature flags / modes:** None – stateless, no configuration.

---

## 2) Runtime Topology (Architecture Overview)

| Component | Stores | Invariants |
|-----------|--------|------------|
| `__HideAdminActions_Chat_Broadcast` | None | Prefix on `Chat.Broadcast`; returns `false` to skip when conditions met |
| `__HideAdminActions_Chat_GetNameColor` | None | Prefix on `Chat.GetNameColor`; sets `__result` and returns `false` to skip vanilla |

**State flow:** No state. Patches are pure prefix logic; no config, data files, or caches.

**Dependencies:** `0Harmony`, `Assembly-CSharp`, `Facepunch.Console` – no Oxide dependencies.

---

## 3) Persistent Data Model

Not applicable. No persistent data; mod is stateless.

---

## 4) Configuration Schema

Not applicable. No configuration. Behavior is fixed in code.

---

## 5) Permissions & Authorization

Not applicable. No permissions. Patches apply globally; all players see the same filtered chat.

---

## 6) Harmony Patches & Event Handling

| Patch | Target | Behavior | Side effects |
|-------|--------|----------|--------------|
| `__HideAdminActions_Chat_Broadcast` | `Chat.Broadcast` | Prefix: returns `false` (skip original) when `username == "SERVER"` and `message.Contains("gave")` | Prevents all give-related broadcasts (NoGiveNotices parity) |
| `__HideAdminActions_Chat_GetNameColor` | `Chat.GetNameColor` | Prefix: sets `__result = "#5af"` and returns `false` (skip original) | All usernames appear cyan instead of admin green |

**Design:** Patches run inline with vanilla chat; no timers, no `NextTick`. Chat filtering is immediate.

---

## 7) Command Surface

Not applicable. No commands.

---

## 8) Lifecycle & State Machine

- **Load:** Harmony applies patches on mod load. No initialization logic.
- **Unload:** Harmony removes patches. No cleanup required.
- **Invariants:** None – stateless.

---

## 9) External API Surface

Not applicable. No public API; no plugin calls.

---

## 10) UI / CUI / Networking

Not applicable. Mod only affects chat broadcast and color; no custom UI.

---

## 11) Gameplay / World Interaction

Not applicable. Mod only modifies chat behavior; no entities, inventory, or world changes.

---

## NoGiveNotices Replacement

This mod fully replaces the Oxide plugin [NoGiveNotices](https://umod.org/plugins/no-give-notices) (by Wulf). That plugin blocks F1 item-giving notices via `OnServerMessage`; HideAdminActions achieves the same via a Harmony prefix on `Chat.Broadcast`, plus name-color hiding.

| NoGiveNotices | HideAdminActions |
|---------------|------------------|
| Blocks `message.Contains("gave")` from SERVER | Same (Chat.Broadcast prefix) |
| No name color change | All names cyan `#5af` (hides admin green) |
| Oxide plugin | Harmony mod (no Oxide dependency) |

You can unload NoGiveNotices and use HideAdminActions instead.

---

## 12) Non-Obvious Design Decisions

- **"SERVER" username:** Admin give messages are broadcast with username `"SERVER"`; the patch uses this to distinguish them from normal player messages.
- **GetNameColor override:** Returning `false` from the prefix skips the original method entirely; `__result` is what callers receive. This hides the green color admins normally get.
- **Message string check:** Uses `Contains("gave")` to match NoGiveNotices – blocks all variants (gave themselves, gave X to Y, gave everyone, gave blueprint, etc.). If game text changes, this may need updating.

---

## 13) What NOT to Touch Without Care

- **Chat.Broadcast conditions:** Tightening the filter (e.g., only blocking more messages) is safe; loosening it could expose admin actions.
- **GetNameColor:** Changing `"#5af"` alters the color all names display as. Ensure it remains a valid chat color hex.
- **Patch order:** Both patches use `Prefix`; order relative to other chat mods could matter if multiple mods patch the same targets.

---

## 14) Performance Anti-Patterns to Avoid

- **Chat.Broadcast:** The `Contains("gave")` check is O(n) on message length; acceptable for chat strings. Do not add expensive logic inside this hot path.
- **GetNameColor:** Patch runs per message/name color lookup; keep it minimal – no reflection, no entity iteration.

---

## Installation

1. Build the mod:
   ```powershell
   .\build.ps1
   ```
2. Deploy: `HideAdminActions.dll` is copied to `D:\!RustServer\HarmonyMods\`.
3. Restart the server (or run `harmony.load HideAdminActions` if supported).

No config files; the mod loads and runs immediately.

## Requirements

- Rust dedicated server with HarmonyLoader (`Rust.Harmony` or equivalent)
- .NET Framework 4.8

## Build

```powershell
.\build.ps1
```

**Output:** `D:\!RustServer\HarmonyMods\HideAdminActions.dll`
