# 0Permissions (Harmony Mod)

Oxide-style **groups + permissions** for Oxide-free servers. Kits, RaidableBases, and other mods check this for access like `kits.defensep3` / `raidablebases.allow`.

Named **`0Permissions`** so filesystem / HarmonyLoader startup order places it before other mods.

## Identity

| Field | Value |
|-------|--------|
| **Name** | 0Permissions (DLL / harmony.load name) |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Config** | `HarmonyConfig/Permissions.json` |
| **Data** | `HarmonyData/Permissions/users.json`, `groups.json` |
| **API** | AppDomain key `Permissions_ApiType` → `PermissionsHarmony.PermissionsMod` |
| **Generation** | AppDomain key `Permissions_Generation` (int, bumps on each load/reload) |
| **Ready callbacks** | `RegisterReadyCallback(Action)` / `UnregisterReadyCallback(Action)` — consumers re-register after 0Permissions loads |

Config/data paths and AppDomain keys stay **`Permissions*`** for compatibility; only the mod/DLL name is `0Permissions`.

## Config

`HarmonyConfig/Permissions.json`:

| Key | Default | Meaning |
|-----|---------|---------|
| `Server Admins Bypass All Permissions` | **`false`** | When `true`, `ownerid`/`moderatorid` (`BasePlayer.IsAdmin`) pass **every** permission check. When `false`, only explicit user/group grants apply. |

**Keep this `false`.** Auth level (console admin / noclip) is separate from mod permissions. With bypass `true`, admins also “have” deny perms such as `raidablebases.banned` and cannot enter raids.

## Groups (manual grants)

- Groups are seeded from `HarmonyConfig/BetterChat.json` (`default`, `admin`, `owner`, VIP tiers, …) plus any you create.
- Every player is added to `default` on connect.
- The **`admin` group grants nothing by default** — put staff in it, then grant only what they need:

```text
perm usergroup add 76561197967147516 admin
perm grant group admin kits.admin
perm grant group admin raidablebases.allow
perm grant group admin raidablebases.config
```

**Do not** grant `*` or `raidablebases.banned` / `raidablebases.buyraid.banned` to staff groups.

Wildcards: `perm grant group admin kits.*` expands to registered kit perms — still avoid bare `*` unless you intend every registered permission including deny ones.

## Registered permission seeding

On load, 0Permissions auto-registers (lists in AdminMenu / `perm show`) from:

| Source | What |
|--------|------|
| Kits | `HarmonyData/Kits/Kits.json` Permission fields + built-ins (`kits.admin`, …) |
| RaidableBases | Built-ins from RB `RegisterPermissions()`, common ladder/buyraid/wipe keys, `HarmonyConfig/RaidableBases.json`, and `HarmonyData/RaidableBases/Profiles/*.json` |
| RaidableBasesUI | `raidablebasesbuyableui.allow`, `.spawn.filenames`, `.spawn.bypass` |
| Shop | Built-ins (`shop.admin`, `shop.free`, `shop.setvm`, …) + `HarmonyConfig/Shop.json` + `HarmonyData/Shop/**/*.json` (`shop.default` / `shop.vip` discounts, category/item perms) |
| Backpacks | Built-ins (`backpacks.use`, `backpacks.admin.*`, `backpacks.gather`, `backpacks.retrieve`, size/profile/restriction/wipe keys, …) + `HarmonyConfig/Backpacks.json` |

RaidableBases / Shop / Backpacks still register their own perms when those mods load; seeding here makes them visible even if they load later or AdminMenu opens first.

## Commands (server console / RCON)

Prefer the **space** form (most reliable on RCON):

```text
perm usergroup add 76561197967147516 admin
perm grant group admin kits.admin
perm grant user 76561197967147516 Kits.admin
perm show user 76561197967147516
perm show group admin
perm show groups
```

Dotted form also works: `perm.usergroup`, `perm.grant`, `perm.show`.

Short aliases: `usergroup`, `grant`, `revoke`.

| Command | Oxide equivalent |
|---------|------------------|
| `perm grant user <name\|steamid> <perm>` | `oxide.grant user …` |
| `perm grant group <group> <perm>` | `oxide.grant group …` |
| `perm revoke user\|group …` | `oxide.revoke …` |
| `perm usergroup add <user> <group>` | `oxide.usergroup add …` |
| `perm usergroup remove <user> <group>` | `oxide.usergroup remove …` |
| `perm group add <group> [title] [rank]` | `oxide.group add …` |
| `perm group remove <group>` | `oxide.group remove …` |
| `perm group set <group> <title> [rank]` | `oxide.group set …` |
| `perm group parent <group> <parent\|none>` | `oxide.group parent …` |
| `perm show user <name\|id>` | `oxide.show user …` |
| `perm show group <group>` | `oxide.show group …` |
| `perm show perm <permission>` | `oxide.show perm …` |
| `perm show groups` / `perm show perms` | `oxide.show groups/perms` |

**Load / reload:**

```text
harmony.reload 0Permissions
```

You should see: `[Permissions] Server admin bypass-all=False …` and `Invoking N ready callback(s)…`.

Consumer mods (AdminMenu, Kits, Backpacks, …) **auto-rebind** when `Permissions_Generation` changes. You do **not** need to `harmony.reload` those mods after reloading 0Permissions. Mods that register permissions subscribe a ready callback so their `RegisterPermission` / group grants run again on 0Permissions load/reload.

The `0` prefix makes this mod sort before other `HarmonyMods/*.dll` names; lazy bind + ready callbacks remain the failsafe if a consumer still loads first.

## Examples (kits)

```text
perm.usergroup add 76561199127409262 vipd
perm.grant group vipd kits.defensep3
perm.grant group vipd kits.defensep2
perm.grant user 76561198255999874 kits.dp1
perm.show user 76561199127409262
perm.show perm kits.defensep3
```

Players without the kit's `Permission` field will not see/redeem that kit (same as Oxide Kits).

## Build

```powershell
.\.cursor\HarmonyMods\0Permissions\build.ps1
```

Load **0Permissions** before consumers when possible. Consumers resolve the API lazily and **auto-rebind** after `harmony.reload 0Permissions` via `Permissions_Generation` + ready callbacks — you do not need to reload AdminMenu / Kits / etc. after 0Permissions.

## Note on Rust `ownerid`

`server/.../cfg/users.cfg` `ownerid` / `moderatorid` still grant **game** admin (F1 console, etc.). That is independent of this mod when bypass is `false`.
