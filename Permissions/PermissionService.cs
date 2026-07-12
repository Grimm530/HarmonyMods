using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PermissionsHarmony
{
    public class UserData
    {
        [JsonProperty("LastSeenNickname")]
        public string LastSeenNickname { get; set; } = "Unnamed";

        [JsonProperty("Perms")]
        public HashSet<string> Perms { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("Groups")]
        public HashSet<string> Groups { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public class GroupData
    {
        [JsonProperty("Title")]
        public string Title { get; set; } = "";

        [JsonProperty("Rank")]
        public int Rank { get; set; }

        [JsonProperty("Perms")]
        public HashSet<string> Perms { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("ParentGroup")]
        public string ParentGroup { get; set; } = "";
    }

    /// <summary>Oxide-style permission store (JSON under HarmonyData/Permissions/).</summary>
    public class PermissionService
    {
        public static PermissionService Instance { get; private set; }

        private readonly string _dataDir;
        private readonly string _usersPath;
        private readonly string _groupsPath;
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, UserData> _users =
            new Dictionary<string, UserData>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, GroupData> _groups =
            new Dictionary<string, GroupData>(StringComparer.OrdinalIgnoreCase);

        private readonly PermissionsConfig _config;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public bool ServerAdminsBypassAll => _config?.ServerAdminsBypassAllPermissions == true;

        public PermissionService(string serverRoot)
        {
            Instance = this;
            _config = PermissionsConfig.LoadOrCreate(serverRoot);
            _dataDir = Path.Combine(serverRoot, "HarmonyData", "Permissions");
            Directory.CreateDirectory(_dataDir);
            _usersPath = Path.Combine(_dataDir, "users.json");
            _groupsPath = Path.Combine(_dataDir, "groups.json");
            Load();
            EnsureGroup("default", "Default", 0);
            EnsureGroup("admin", "[Admin]", 1);
            SeedFromBetterChat(serverRoot);
            SeedKitPermissions(serverRoot);
            SeedRaidableBasesPermissions(serverRoot);
            SeedShopPermissions(serverRoot);
            SeedBackpacksPermissions(serverRoot);
            Save();
            Debug.Log($"[Permissions] Server admin bypass-all={ServerAdminsBypassAll} (HarmonyConfig/Permissions.json). Grants are group/user only when false.");
        }

        public void Shutdown()
        {
            Save();
            if (Instance == this) Instance = null;
        }

        #region Persistence

        private void Load()
        {
            try
            {
                if (File.Exists(_usersPath))
                {
                    var raw = File.ReadAllText(_usersPath);
                    _users = JsonConvert.DeserializeObject<Dictionary<string, UserData>>(raw, JsonSettings)
                             ?? new Dictionary<string, UserData>(StringComparer.OrdinalIgnoreCase);
                    _users = new Dictionary<string, UserData>(_users, StringComparer.OrdinalIgnoreCase);
                    foreach (var u in _users.Values)
                    {
                        u.Perms = new HashSet<string>(u.Perms ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        u.Groups = new HashSet<string>(u.Groups ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Permissions] Failed to load users.json: " + ex.Message);
                _users = new Dictionary<string, UserData>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                if (File.Exists(_groupsPath))
                {
                    var raw = File.ReadAllText(_groupsPath);
                    _groups = JsonConvert.DeserializeObject<Dictionary<string, GroupData>>(raw, JsonSettings)
                              ?? new Dictionary<string, GroupData>(StringComparer.OrdinalIgnoreCase);
                    _groups = new Dictionary<string, GroupData>(_groups, StringComparer.OrdinalIgnoreCase);
                    foreach (var g in _groups.Values)
                    {
                        g.Perms = new HashSet<string>(g.Perms ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        g.ParentGroup ??= "";
                        g.Title ??= "";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Permissions] Failed to load groups.json: " + ex.Message);
                _groups = new Dictionary<string, GroupData>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_usersPath, JsonConvert.SerializeObject(_users, JsonSettings));
                File.WriteAllText(_groupsPath, JsonConvert.SerializeObject(_groups, JsonSettings));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Permissions] Save failed: " + ex.Message);
            }
        }

        private void SaveUsers() => Save();
        private void SaveGroups() => Save();

        #endregion

        #region Seeding

        private void SeedFromBetterChat(string serverRoot)
        {
            var path = Path.Combine(serverRoot, "HarmonyConfig", "BetterChat.json");
            if (!File.Exists(path)) return;
            try
            {
                var arr = JArray.Parse(File.ReadAllText(path));
                int added = 0;
                foreach (var token in arr)
                {
                    var name = token["GroupName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var title = token["Title"]?["Text"]?.ToString() ?? name;
                    var priority = token["Priority"]?.Value<int>() ?? 0;
                    if (!_groups.ContainsKey(name))
                    {
                        EnsureGroup(name, title, priority);
                        added++;
                    }
                    else
                    {
                        // Keep existing perms; refresh title/rank if empty
                        var g = _groups[name];
                        if (string.IsNullOrEmpty(g.Title)) g.Title = title;
                    }
                }
                if (added > 0)
                    Debug.Log($"[Permissions] Seeded {added} groups from BetterChat.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] BetterChat seed: " + ex.Message);
            }
        }

        private void SeedKitPermissions(string serverRoot)
        {
            var path = Path.Combine(serverRoot, "HarmonyData", "Kits", "Kits.json");
            if (!File.Exists(path)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var kits = root["Kits"] as JArray;
                if (kits == null) return;
                int count = 0;
                foreach (var kit in kits)
                {
                    var perm = kit["Permission"]?.ToString();
                    if (string.IsNullOrWhiteSpace(perm)) continue;
                    if (_registered.Add(perm)) count++;
                }
                // Also register common Kits built-ins
                RegisterPermission("kits.admin");
                RegisterPermission("kits.dlc");
                RegisterPermission("kits.bypasscooldown");
                RegisterPermission("kits.bypasslimit");
                RegisterPermission("kits.changeautokit");
                Debug.Log($"[Permissions] Registered {count} kit permissions from Kits.json (total registered={_registered.Count})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] Kits seed: " + ex.Message);
            }
        }

        /// <summary>
        /// Register RaidableBases (+ Buyable UI) permissions so AdminMenu / perm show list them
        /// even if RaidableBases.dll has not loaded yet. Built-ins + HarmonyConfig + Profiles.
        /// </summary>
        private void SeedRaidableBasesPermissions(string serverRoot)
        {
            int before = _registered.Count;

            // Hardcoded RegisterPermissions() list from RaidableBases.Helpers.cs
            string[] builtIns =
            {
                "raidablebases.allow",
                "raidablebases.allow.commands",
                "raidablebases.bypassmaxmanualeventlimit",
                "raidablebases.setowner",
                "raidablebases.clearowner",
                "raidablebases.ladder.exclude",
                "raidablebases.durabilitybypass",
                "raidablebases.ddraw",
                "raidablebases.mapteleport",
                "raidablebases.canbypass",
                "raidablebases.lockoutbypass",
                "raidablebases.blockbypass",
                "raidablebases.banned",
                "raidablebases.vipcooldown",
                "raidablebases.despawn.buyraid",
                "raidablebases.notitle",
                "raidablebases.block.fauxadmin",
                "raidablebases.elevators.bypass.building",
                "raidablebases.elevators.bypass.card",
                "raidablebases.time",
                "raidablebases.timebypass",
                "raidablebases.buyraid",
                "raidablebases.buyraid.free",
                "raidablebases.buyraid.banned",
                "raidablebases.buyraid.prefabteleport",
                "raidablebases.buyable.bypass.cooldown",
                "raidablebases.buyable.spawn.filenames",
                "raidablebases.buyable.vip.pve",
                "raidablebases.buyable.vip.pvp",
                "raidablebases.hoggingbypass",
                "raidablebases.block.filenames",
                "raidablebases.keepbackpackplugin",
                "raidablebases.keepbackpackrust",
                "raidablebases.buyraid.pvponly",
                "raidablebases.buyraid.pveonly",
                "raidablebases.invitecommand",
                "raidablebases.limitedannouncements",
                "raidablebases.config",
                // Buyable UI companion
                "raidablebasesbuyableui.allow",
                "raidablebasesbuyableui.spawn.filenames",
                "raidablebasesbuyableui.spawn.bypass"
            };

            foreach (var perm in builtIns)
                RegisterPermission(perm);

            // Common defaults often present in config even before first RB load
            string[] commonConfigDefaults =
            {
                "raidablebases.th",
                "raidablebases.ladder.easy",
                "raidablebases.ladder.medium",
                "raidablebases.ladder.hard",
                "raidablebases.ladder.expert",
                "raidablebases.ladder.nightmare",
                "raidablebases.ladder.buybase",
                "raidablebases.ladder.sky",
                "raidablebases.ladder.water",
                "raidablebases.buyraid.easywipetime",
                "raidablebases.buyraid.mediumwipetime",
                "raidablebases.buyraid.hardwipetime",
                "raidablebases.buyraid.expertwipetime",
                "raidablebases.buyraid.nightmarewipetime",
                "raidablebases.buyraid.skywipetime",
                "raidablebases.buyraid.waterwipetime"
            };
            foreach (var perm in commonConfigDefaults)
                RegisterPermission(perm);

            try
            {
                var configPath = Path.Combine(serverRoot, "HarmonyConfig", "RaidableBases.json");
                if (File.Exists(configPath))
                    SeedRaidableBasesPermissionStringsFromText(File.ReadAllText(configPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] RaidableBases config seed: " + ex.Message);
            }

            try
            {
                var profilesDir = Path.Combine(serverRoot, "HarmonyData", "RaidableBases", "Profiles");
                if (Directory.Exists(profilesDir))
                {
                    foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try { SeedRaidableBasesPermissionStringsFromText(File.ReadAllText(file)); }
                        catch { /* skip bad profile */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] RaidableBases profile seed: " + ex.Message);
            }

            int added = _registered.Count - before;
            Debug.Log($"[Permissions] Registered RaidableBases permissions (+{added}; total registered={_registered.Count})");
        }

        private static readonly Regex RaidableBasesPermRegex = new Regex(
            @"\b((?:raidablebases|raidablebasesbuyableui)\.[a-zA-Z0-9_.]+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void SeedRaidableBasesPermissionStringsFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in RaidableBasesPermRegex.Matches(text))
            {
                string perm = m.Groups[1].Value;
                // Translate phrase key, not a grantable permission
                if (perm.Equals("raidablebases.tip", StringComparison.OrdinalIgnoreCase))
                    continue;
                RegisterPermission(perm);
            }
        }

        /// <summary>
        /// Register Shop permissions so AdminMenu / perm show list them even if Shop.dll
        /// has not loaded yet. Built-ins + HarmonyConfig/Shop.json + HarmonyData/Shop.
        /// </summary>
        private void SeedShopPermissions(string serverRoot)
        {
            int before = _registered.Count;

            string[] builtIns =
            {
                "shop.admin",
                "shop.free",
                "shop.setvm",
                "shop.setnpc",
                "shop.bypass.dlc",
                "shop.use",
                "shop.usenpc",
                "shop.default",
                "shop.vip",
                "shop.buyagain"
            };

            foreach (var perm in builtIns)
                RegisterPermission(perm);

            try
            {
                var configPath = Path.Combine(serverRoot, "HarmonyConfig", "Shop.json");
                if (File.Exists(configPath))
                    SeedShopPermissionStringsFromText(File.ReadAllText(configPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] Shop config seed: " + ex.Message);
            }

            try
            {
                var dataDir = Path.Combine(serverRoot, "HarmonyData", "Shop");
                if (Directory.Exists(dataDir))
                {
                    foreach (var file in Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories))
                    {
                        // Skip log dumps / huge non-shop catalogs if any — still JSON of shop data
                        string name = Path.GetFileName(file) ?? "";
                        if (name.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        try { SeedShopPermissionStringsFromText(File.ReadAllText(file)); }
                        catch { /* skip bad file */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] Shop data seed: " + ex.Message);
            }

            int added = _registered.Count - before;
            Debug.Log($"[Permissions] Registered Shop permissions (+{added}; total registered={_registered.Count})");
        }

        private static readonly Regex ShopPermRegex = new Regex(
            @"\b(shop\.[a-zA-Z0-9_.]+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void SeedShopPermissionStringsFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in ShopPermRegex.Matches(text))
            {
                string perm = m.Groups[1].Value;
                // Ignore trailing-dot junk and console command names that are not grant keys
                if (perm.EndsWith(".", StringComparison.Ordinal)) continue;
                if (perm.Equals("shop.", StringComparison.OrdinalIgnoreCase)) continue;
                RegisterPermission(perm.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Register Backpacks permissions so AdminMenu / perm show list them even if
        /// Backpacks.dll has not loaded yet. Built-ins + HarmonyConfig/Backpacks.json.
        /// </summary>
        private void SeedBackpacksPermissions(string serverRoot)
        {
            int before = _registered.Count;

            string[] builtIns =
            {
                "backpacks.admin",
                "backpacks.admin.view",
                "backpacks.admin.edit",
                "backpacks.admin.resize",
                "backpacks.admin.debug",
                "backpacks.admin.protected",
                "backpacks.use",
                "backpacks.gui",
                "backpacks.fetch",
                "backpacks.gather",
                "backpacks.retrieve",
                "backpacks.keepondeath",
                "backpacks.nofoodspoiling",
                "backpacks.keeponwipe",
                "backpacks.noblacklist",
                // Legacy row sizes (Enable legacy backpacks.use.1-8)
                "backpacks.use.1",
                "backpacks.use.2",
                "backpacks.use.3",
                "backpacks.use.4",
                "backpacks.use.5",
                "backpacks.use.6",
                "backpacks.use.7",
                "backpacks.use.8",
                // Common size + profile + restriction + wipe keys from default config
                "backpacks.size.6",
                "backpacks.size.12",
                "backpacks.size.18",
                "backpacks.size.24",
                "backpacks.size.30",
                "backpacks.size.36",
                "backpacks.size.42",
                "backpacks.size.48",
                "backpacks.size.96",
                "backpacks.size.144",
                "backpacks.size.profile.6-48",
                "backpacks.size.profile.6-96",
                "backpacks.size.profile.6-144",
                "backpacks.restrictions.allowall",
                "backpacks.keeponwipe.all"
            };

            foreach (var perm in builtIns)
                RegisterPermission(perm);

            try
            {
                var configPath = Path.Combine(serverRoot, "HarmonyConfig", "Backpacks.json");
                if (File.Exists(configPath))
                    SeedBackpacksPermissionStringsFromText(File.ReadAllText(configPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] Backpacks config seed: " + ex.Message);
            }

            int added = _registered.Count - before;
            Debug.Log($"[Permissions] Registered Backpacks permissions (+{added}; total registered={_registered.Count})");
        }

        private static readonly Regex BackpacksPermRegex = new Regex(
            @"\b(backpacks\.[a-zA-Z0-9_.]+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void SeedBackpacksPermissionStringsFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in BackpacksPermRegex.Matches(text))
            {
                string perm = m.Groups[1].Value;
                if (perm.EndsWith(".", StringComparison.Ordinal)) continue;
                RegisterPermission(perm.ToLowerInvariant());
            }

            // Config Permission sizes / suffixes often appear as bare numbers or name fields —
            // also seed backpacks.size.N and backpacks.size.profile.SUFFIX / restrictions.NAME / keeponwipe.NAME
            try
            {
                var jo = JObject.Parse(text);
                var sizes = jo["Backpack size"]?["Permission sizes"] as JArray;
                if (sizes != null)
                {
                    foreach (var t in sizes)
                    {
                        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                            RegisterPermission("backpacks.size." + t.ToString());
                    }
                }
                var profiles = jo["Backpack size"]?["Dynamic Size (EXPERIMENTAL)"]?["Size profiles"] as JArray;
                if (profiles != null)
                {
                    foreach (var p in profiles)
                    {
                        var suffix = p?["Permission suffix"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(suffix))
                            RegisterPermission("backpacks.size.profile." + suffix.Trim());
                    }
                }
                SeedNamedRulesetPerms(jo["Item restrictions"]?["Rulesets by permission"] as JArray, "backpacks.restrictions.");
                SeedNamedRulesetPerms(jo["Clear on wipe"]?["Rulesets by permission"] as JArray, "backpacks.keeponwipe.");
            }
            catch { /* ignore parse errors; built-ins already registered */ }
        }

        private void SeedNamedRulesetPerms(JArray rulesets, string prefix)
        {
            if (rulesets == null) return;
            foreach (var r in rulesets)
            {
                var name = r?["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    RegisterPermission(prefix + name.Trim());
            }
        }

        #endregion

        #region Register

        public void RegisterPermission(string perm)
        {
            if (!string.IsNullOrEmpty(perm))
                _registered.Add(perm);
        }

        public bool PermissionExists(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            if (perm == "*") return true;
            if (perm.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = perm.TrimEnd('*');
                return _registered.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            return _registered.Contains(perm);
        }

        public IEnumerable<string> GetPermissions() => _registered.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Users / Groups lookups

        private UserData GetOrCreateUser(string playerId)
        {
            if (!_users.TryGetValue(playerId, out var user))
            {
                user = new UserData();
                _users[playerId] = user;
            }
            if (!user.Groups.Contains("default") && GroupExists("default"))
                user.Groups.Add("default");
            return user;
        }

        public bool GroupExists(string groupName) =>
            !string.IsNullOrEmpty(groupName) && (groupName == "*" || _groups.ContainsKey(groupName));

        public bool EnsureGroup(string groupName, string title = "", int rank = 0)
        {
            if (string.IsNullOrWhiteSpace(groupName) || groupName == "*") return false;
            if (_groups.ContainsKey(groupName)) return false;
            _groups[groupName] = new GroupData
            {
                Title = title ?? "",
                Rank = rank,
                Perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
            return true;
        }

        public bool CreateGroup(string groupName, string title, int rank)
        {
            if (!EnsureGroup(groupName, title, rank)) return false;
            SaveGroups();
            return true;
        }

        public bool RemoveGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName) || groupName.Equals("default", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!_groups.Remove(groupName)) return false;
            foreach (var user in _users.Values)
                user.Groups.Remove(groupName);
            foreach (var g in _groups.Values)
            {
                if (string.Equals(g.ParentGroup, groupName, StringComparison.OrdinalIgnoreCase))
                    g.ParentGroup = "";
            }
            Save();
            return true;
        }

        public bool SetGroupTitle(string groupName, string title)
        {
            if (!_groups.TryGetValue(groupName, out var g)) return false;
            g.Title = title ?? "";
            SaveGroups();
            return true;
        }

        public bool SetGroupRank(string groupName, int rank)
        {
            if (!_groups.TryGetValue(groupName, out var g)) return false;
            g.Rank = rank;
            SaveGroups();
            return true;
        }

        public bool SetGroupParent(string groupName, string parentGroup)
        {
            if (!_groups.TryGetValue(groupName, out var g)) return false;
            if (!string.IsNullOrEmpty(parentGroup) && !GroupExists(parentGroup)) return false;
            if (!string.IsNullOrEmpty(parentGroup) && HasCircularParent(groupName, parentGroup)) return false;
            g.ParentGroup = parentGroup ?? "";
            SaveGroups();
            return true;
        }

        private bool HasCircularParent(string groupName, string parent)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { groupName };
            var current = parent;
            while (!string.IsNullOrEmpty(current))
            {
                if (!seen.Add(current)) return true;
                if (!_groups.TryGetValue(current, out var g)) break;
                current = g.ParentGroup;
            }
            return false;
        }

        public IEnumerable<string> GetGroups() => _groups.Keys.OrderBy(g => g, StringComparer.OrdinalIgnoreCase);

        public GroupData GetGroupData(string groupName) =>
            _groups.TryGetValue(groupName, out var g) ? g : null;

        public UserData GetUserData(string playerId) =>
            _users.TryGetValue(playerId, out var u) ? u : null;

        public IEnumerable<string> GetUsersInGroup(string groupName)
        {
            foreach (var kv in _users)
            {
                if (kv.Value.Groups.Contains(groupName))
                    yield return kv.Key;
            }
        }

        #endregion

        #region Membership

        public bool UserHasGroup(string playerId, string groupName)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(groupName)) return false;
            if (!_users.TryGetValue(playerId, out var user)) return false;
            return user.Groups.Contains(groupName);
        }

        public bool AddUserGroup(string playerId, string groupName)
        {
            if (!GroupExists(groupName) || groupName == "*") return false;
            var user = GetOrCreateUser(playerId);
            if (!user.Groups.Add(groupName)) return false;
            SaveUsers();
            return true;
        }

        public bool RemoveUserGroup(string playerId, string groupName)
        {
            if (!_users.TryGetValue(playerId, out var user)) return false;
            if (groupName.Equals("default", StringComparison.OrdinalIgnoreCase)) return false;
            if (!user.Groups.Remove(groupName)) return false;
            SaveUsers();
            return true;
        }

        public void TouchUser(string playerId, string nickname)
        {
            var user = GetOrCreateUser(playerId);
            if (!string.IsNullOrEmpty(nickname))
                user.LastSeenNickname = nickname;
        }

        #endregion

        #region Grant / Revoke

        public bool GrantUserPermission(string playerId, string permission)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(permission)) return false;
            var user = GetOrCreateUser(playerId);
            bool changed = GrantToSet(user.Perms, permission);
            if (changed) SaveUsers();
            return changed;
        }

        public bool RevokeUserPermission(string playerId, string permission)
        {
            if (!_users.TryGetValue(playerId, out var user)) return false;
            bool changed = RevokeFromSet(user.Perms, permission);
            if (changed) SaveUsers();
            return changed;
        }

        public bool GrantGroupPermission(string groupName, string permission)
        {
            if (!_groups.TryGetValue(groupName, out var group)) return false;
            bool changed = GrantToSet(group.Perms, permission);
            if (changed) SaveGroups();
            return changed;
        }

        public bool RevokeGroupPermission(string groupName, string permission)
        {
            if (!_groups.TryGetValue(groupName, out var group)) return false;
            bool changed = RevokeFromSet(group.Perms, permission);
            if (changed) SaveGroups();
            return changed;
        }

        private bool GrantToSet(HashSet<string> set, string permission)
        {
            if (permission == "*")
            {
                bool any = false;
                foreach (var p in _registered)
                    any |= set.Add(p);
                any |= set.Add("*");
                return any;
            }
            if (permission.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = permission.TrimEnd('*');
                bool any = false;
                foreach (var p in _registered)
                {
                    if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        any |= set.Add(p);
                }
                any |= set.Add(permission);
                return any;
            }
            RegisterPermission(permission);
            return set.Add(permission);
        }

        private bool RevokeFromSet(HashSet<string> set, string permission)
        {
            if (permission == "*")
            {
                int before = set.Count;
                set.Clear();
                return before > 0;
            }
            if (permission.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = permission.TrimEnd('*');
                return set.RemoveWhere(p =>
                    p.Equals(permission, StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            return set.Remove(permission);
        }

        #endregion

        #region HasPermission

        public bool GroupHasPermission(string groupName, string permission)
        {
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(permission)) return false;
            if (!_groups.TryGetValue(groupName, out var group)) return false;
            if (SetHasPermission(group.Perms, permission)) return true;
            if (!string.IsNullOrEmpty(group.ParentGroup))
                return GroupHasPermission(group.ParentGroup, permission);
            return false;
        }

        public bool UserHasPermission(string playerId, string permission)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            if (string.IsNullOrEmpty(permission)) return true;

            // Optional: ownerid/moderatorid → IsAdmin pass every check (including deny perms like *.banned).
            // Default off — assign the admin group and grant only the perms you want.
            if (ServerAdminsBypassAll && IsServerAdmin(playerId)) return true;

            var user = GetOrCreateUser(playerId);
            if (SetHasPermission(user.Perms, permission)) return true;

            foreach (var groupName in user.Groups)
            {
                if (GroupHasPermission(groupName, permission))
                    return true;
            }
            return false;
        }

        private static bool SetHasPermission(HashSet<string> set, string permission)
        {
            if (set == null || set.Count == 0) return false;
            if (set.Contains("*")) return true;
            if (set.Contains(permission)) return true;
            // granted prefix wildcards e.g. kits.*
            foreach (var p in set)
            {
                if (p.EndsWith("*", StringComparison.Ordinal) &&
                    permission.StartsWith(p.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsServerAdmin(string playerId)
        {
            if (!ulong.TryParse(playerId, out var uid)) return false;
            var p = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
            return p != null && p.IsAdmin;
        }

        #endregion

        #region Query helpers for show commands

        public IEnumerable<string> GetUsersWithPermission(string permission)
        {
            foreach (var kv in _users)
            {
                if (UserHasPermission(kv.Key, permission))
                    yield return kv.Key;
            }
        }

        public IEnumerable<string> GetGroupsWithPermission(string permission)
        {
            foreach (var name in _groups.Keys)
            {
                if (GroupHasPermission(name, permission))
                    yield return name;
            }
        }

        public string ResolvePlayerId(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            nameOrId = nameOrId.Trim();
            if (ulong.TryParse(nameOrId, out _))
                return nameOrId;

            foreach (var kv in _users)
            {
                if (string.Equals(kv.Value.LastSeenNickname, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p != null && string.Equals(p.displayName, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return p.UserIDString;
            }
            foreach (var p in BasePlayer.sleepingPlayerList)
            {
                if (p != null && string.Equals(p.displayName, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return p.UserIDString;
            }
            return null;
        }

        #endregion
    }
}
