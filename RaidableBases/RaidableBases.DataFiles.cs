using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Data files

        private bool ProfilesExists()
        {
            try
            {
                HarmonyDataLayer.GetFiles(Path.Combine(Name, "Profiles"));

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CreateDefaultFiles()
        {
            if (ProfilesExists())
            {
                return;
            }

            HarmonyDataLayer.GetDatafile(Path.Combine(Name, "Profiles", "_emptyfile"));

            foreach (var (key, options) in DefaultBuildingOptions())
            {
                string filename = Path.Combine(Name, "Profiles", key);

                if (!HarmonyDataLayer.ExistsDatafile(filename))
                {
                    SaveProfile(key, options);
                }
            }

            string lootFile = Path.Combine(Name, "Default_Loot");

            if (!HarmonyDataLayer.ExistsDatafile(lootFile))
            {
                var defaultLoot = DefaultLoot();
                defaultLoot.ForEach(ti => ti.InitializeArmorSlots());
                HarmonyDataLayer.WriteObject(lootFile, defaultLoot);
            }
        }

        private List<string> profileErrors = new();
        private bool AnyCopyPasteFileExists;

        protected IEnumerator LoadProfiles(DisposableBuilder _sb, IPlayer user = null)
        {
            string folder = Path.Combine(Name, "Profiles");
            string[] files = GetProfileFiles();

            if (files.Length == 0)
            {
                yield break;
            }

            ProcessExtensions(ExtOp.Invalidate);
            RaidableModes.Clear();
            Dictionary<string, BaseProfile> previousProfiles = Buildings.Profiles;
            Buildings.Profiles = new(StringComparer.OrdinalIgnoreCase);
            GridController.SpawnCache.Clear();

            bool grey = false, allProfilesPVP = true;

            foreach (string file in files)
            {
                yield return CoroutineEx.waitForFixedUpdate;

                string profileName = GetFileNameWithoutExtension(file);

                try
                {
                    if (file.Contains("_empty"))
                    {
                        continue;
                    }

                    var path = Path.Combine(folder, profileName);
                    var options = HarmonyDataLayer.ReadObject<BuildingOptions>(path);

                    if (options == null)
                    {
                        continue;
                    }

                    options.AllowedWeaponAndAmmoShortnames ??= new();
                    HashSet<string> acceptedItems = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = options.AllowedWeaponAndAmmoShortnames.Count - 1; i >= 0; i--)
                    {
                        string shortname = options.AllowedWeaponAndAmmoShortnames[i]?.Trim();
                        if (string.IsNullOrWhiteSpace(shortname) || !acceptedItems.Add(shortname))
                        {
                            options.AllowedWeaponAndAmmoShortnames.RemoveAt(i);
                        }
                        else options.AllowedWeaponAndAmmoShortnames[i] = shortname;
                    }
                    options.AllowedWeaponAndAmmoShortnames.Sort(StringComparer.OrdinalIgnoreCase);
                    options.AdditionalBases ??= new();

                    if (options._AdditionalBases != null)
                    {
                        foreach (var (baseName, pasteOptions) in options._AdditionalBases)
                        {
                            options.AdditionalBases.Add(baseName, new()
                            {
                                Options = pasteOptions,
                                Costs = DefaultCostOptions()
                            });
                        }
                        options._AdditionalBases = null;
                    }

                    if (options._EnforceDurability != null)
                    {
                        options.EnforceConditionLoss = options._EnforceDurability.Value;
                        options._EnforceDurability = null;
                    }

                    if (options.AutoTurret._Shortnames != null)
                    {
                        options.AutoTurret._Shortnames.RemoveAll(string.IsNullOrWhiteSpace);
                        options.AutoTurret.Shortnames ??= new();
                        options.AutoTurret.Shortnames.Clear();
                        foreach (var weapon in options.AutoTurret._Shortnames)
                        {
                            options.AutoTurret.Shortnames[weapon] = new() { 0 };
                        }
                        options.AutoTurret._Shortnames = null;
                    }

                    if (!string.IsNullOrWhiteSpace(options.CustomSpawns._SpawnsFile))
                    {
                        options.CustomSpawns.BuyableSpawnsFile = options.CustomSpawns._SpawnsFile;
                        options.CustomSpawns.MaintainedSpawnsFile = options.CustomSpawns._SpawnsFile;
                        options.CustomSpawns.ScheduledSpawnsFile = options.CustomSpawns._SpawnsFile;
                        options.CustomSpawns._SpawnsFile = null;
                    }

                    if (allowBuilding.HasValue)
                    {
                        options.AllowBuilding = allowBuilding.Value;
                    }

                    if (allowBuildingBlockExceptions != null)
                    {
                        options.AllowedBuildingBlockExceptions = allowBuildingBlockExceptions.ToList();
                    }

                    if (!config.Settings._BlacklistedPVECommands.IsNullOrEmpty())
                    {
                        options.BlacklistedPVECommands = config.Settings._BlacklistedPVECommands.ToList();
                    }

                    if (!config.Settings._BlacklistedPVPCommands.IsNullOrEmpty())
                    {
                        options.BlacklistedPVPCommands = config.Settings._BlacklistedPVPCommands.ToList();
                    }

                    if (config.Settings.Management._Mounts != null)
                    {
                        options.Mounts = config.Settings.Management._Mounts;
                    }

                    if (config.Settings.Management._Biomes != null && options.Biomes == null)
                    {
                        options.Biomes = config.Settings.Management._Biomes;
                    }

                    options.Biomes ??= new();

                    if (!options.Setup.EnableForcedHeight)
                    {
                        options.Setup.ForcedHeightValue = -1;
                    }

                    if (options.BuoyantBox)
                    {
                        BuoyantBox = true;
                    }

                    if (!options.AllowPVP)
                    {
                        allProfilesPVP = false;
                    }

                    if (options.NPC.Accuracy.MINIGUN == 0)
                    {
                        options.NPC.Accuracy.MINIGUN = options.NPC.Accuracy.M249;
                    }

                    if (options.Rewards.XPerience == -125)
                    {
                        options.Rewards.XPerience = options.Rewards.SkillTree;
                    }

                    if (options.Rewards.XLevels == -125)
                    {
                        options.Rewards.XLevels = options.Rewards.SkillTree;
                    }

                    grey |= options.DespawnGreyBoxBags;
                    options.Siege.Disabled = !options.Siege.Any;
                    options.ExplosionModifier = Mathf.Clamp(options.ExplosionModifier, 0f, 999f);
                    options.Permission.Register(this, permission);
                    options.DrawLoot.Register(this, permission);
                    options.CustomSpawns.BuyableTeleportPrefabs.Remove("");
                    //options.CustomSpawns.SpawnPointPrefabs.Remove("");
                    options.BlockedEntityDamage.RemoveAll(string.IsNullOrWhiteSpace);

                    if (previousProfiles.TryGetValue(profileName, out var profile))
                    {
                        profile.BaseLootList = new();
                        profile.Options = options;
                        profile.ProfileName = profileName;
                        profile.Spawns = new();
                    }
                    else
                    {
                        profile = new(this, options, profileName);
                    }

                    Buildings.Profiles[profileName] = profile;
                }
                catch (Exception ex)
                {
                    Puts("{0}\n{1}", file, ex);
                    profileErrors.Add(file);
                    continue;
                }
            }

            bool saveConfig = false;

            if (config.Settings.Management._Mounts != null)
            {
                config.Settings.Management._Mounts = null;
                saveConfig = true;
            }

            if (config.Settings._BlacklistedPVECommands != null)
            {
                config.Settings._BlacklistedPVECommands = null;
                saveConfig = true;
            }

            if (config.Settings._BlacklistedPVPCommands != null)
            {
                config.Settings._BlacklistedPVPCommands = null;
                saveConfig = true;
            }

            if (config.Settings.Management.DropLoot.SET != null)
            {
                config.Settings.Management.DropLoot.DespawnGreyWeaponBags = grey;
                config.Settings.Management.DropLoot.SET = null;
                saveConfig = true;
            }

            if (config.Settings.Management._Biomes != null)
            {
                config.Settings.Management._Biomes = null;
                saveConfig = true;
            }

            foreach (var opt in config.Settings.Buyable.Cooldowns.Dictionary.Values)
            {
                if (opt.VIP.HasValue)
                {
                    opt.Permissions["raidablebases.vipcooldown"] = opt.VIP.Value;
                    opt.VIP = null;
                    saveConfig = true;
                }
            }

            if (saveConfig)
            {
                SaveConfig();
                yield return null;
            }

            Dictionary<int, (string mode, int count)> levels = new();
            Dictionary<string, HashSet<Vector3>> modes = new();
            using var tmp = Buildings.Profiles.ToPooledList();
            using var sb = DisposableBuilder.Get();
            using var customSpawnTypes = DisposableList<RaidableType>();
            customSpawnTypes.Add(RaidableType.Purchased);
            customSpawnTypes.Add(RaidableType.Maintained);
            customSpawnTypes.Add(RaidableType.Scheduled);
            bool allowPVP = false;
            bool allowPVE = false;

            foreach (var (key, profile) in tmp)
            {
                if (!AnyCopyPasteFileExists && (FileExists(key) || profile.Options.AdditionalBases.Keys.Exists(FileExists)))
                {
                    AnyCopyPasteFileExists = true;
                }

                allowPVP |= profile.Options.AllowPVP;
                allowPVE |= !profile.Options.AllowPVP;

                if (!string.IsNullOrWhiteSpace(profile.Options.ObsoleteMode))
                {
                    profile.Options.Mode = profile.Options.ObsoleteMode switch
                    {
                        "4" => RaidableMode.Nightmare,
                        "3" => RaidableMode.Expert,
                        "2" => RaidableMode.Hard,
                        "1" => RaidableMode.Medium,
                        _ => RaidableMode.Easy
                    };

                    profile.Options.Level = int.TryParse(profile.Options.ObsoleteMode, out int value) ? value : 0;
                    profile.Options.ObsoleteMode = null;
                    profile.Options.Setup.ForcedHeightValue = -1;
                }

                if (string.IsNullOrWhiteSpace(profile.Options.Mode))
                {
                    bool upgrade = string.IsNullOrWhiteSpace(profile.Options.ObsoleteMode) && profile.Options.Level == -1;
                    profile.Options.Mode = RaidableMode.Easy;
                    profile.Options.Level = 0;
                    profile.Options.Setup.ForcedHeightValue = -1;
                    if (upgrade)
                    {
                        config.Settings.Management.Colors1.Remove("Normal");
                        config.Settings.Management.Colors2.Remove("Normal");
                        TryInvokeMethod(() => ModifyDifficultyMode(_consolePlayer, RaidableMode.Easy, true, false, "00FF00", key));
                        TryInvokeMethod(() => ModifyDifficultyMode(_consolePlayer, RaidableMode.Medium, true, false, "FFEB04"));
                        TryInvokeMethod(() => ModifyDifficultyMode(_consolePlayer, RaidableMode.Hard, true, false, "FF0000"));
                        TryInvokeMethod(() => ModifyDifficultyMode(_consolePlayer, RaidableMode.Expert, true, false, "0000FF"));
                        TryInvokeMethod(() => ModifyDifficultyMode(_consolePlayer, RaidableMode.Nightmare, true, false, "000000"));
                        ProcessExtensions(ExtOp.Init);
                    }
                }

                profile.Options.NPC.SetAccuracy(profile.Options.Mode);

                yield return CoroutineEx.waitForFixedUpdate;

                foreach (RaidableType type in customSpawnTypes)
                {
                    var spawnsFile = profile.Options.CustomSpawns.Get(type);
                    if (GridController.SpawnsFileValid(spawnsFile))
                    {
                        if (!GridController.SpawnCache.TryGetValue(spawnsFile, out var spawns))
                        {
                            spawns = GridController.GetSpawnsLocations(spawnsFile);

                            if (spawns.Count > 0)
                            {
                                GridController.SpawnCache[spawnsFile] = spawns;
                                if (type == RaidableType.Purchased) profile.Options.CustomSpawns.BuyableSpawnsFileSet = true;
                                if (type == RaidableType.Maintained) profile.Options.CustomSpawns.MaintainedSpawnsFileSet = true;
                                if (type == RaidableType.Scheduled) profile.Options.CustomSpawns.ScheduledSpawnsFileSet = true;
                            }
                        }

                        if (spawns.Count > 0)
                        {
                            modes.TryAdd(profile.Options.Mode, new());
                            foreach (var x in spawns)
                            {
                                modes[profile.Options.Mode].Add(x.Location);
                                if (x.Location.y > MaxTerrainY) MaxTerrainY = x.Location.y + 1f;
                            }

                            profile.Spawns ??= new();
                            profile.Spawns[type] = new(this, spawns);

                            if (profile.Options.CustomSpawns.PreventBuilding)
                            {
                                Subscribe(nameof(CanBuild));
                            }

                            profile.Options.CustomSpawns.BuyableTeleportRadius = profile.Options.ProtectionRadius(RaidableType.Purchased);
                        }

                        yield return CoroutineEx.waitForFixedUpdate;
                    }
                }

                if (levels.TryGetValue(profile.Options.Level, out var info))
                {
                    if (info.mode != profile.Options.Mode && sb.Length == 0)
                    {
                        sb.AppendLine($"Invalid profiles: 'Difficulty Level: {profile.Options.Level}' is shared by '{info.mode}' and '{profile.Options.Mode}'");
                    }

                    levels[profile.Options.Level] = (info.mode, info.count + 1);
                }
                else
                {
                    levels[profile.Options.Level] = (profile.Options.Mode, 1);
                }

                SaveProfile(key, profile.Options);
                yield return CoroutineEx.waitForFixedUpdate;
            }

            foreach (var mode in modes)
            {
                Puts(mx("LoadedDifficulty", null, mode.Value.Count, mode.Key));
            }

            if (sb.Length > 0)
            {
                Puts(sb.ToString());
            }

            if (config.Settings.Maintained.Enabled)
            {
                if (allowPVP && !config.Settings.Maintained.IncludePVP && !allowPVE)
                {
                    Puts("Invalid configuration: Maintained Events -> Include PVP Bases is set false when all profiles have Allow PVP set to true. You can set Include PVP Bases to true, and Convert PVP To PVE to true.");
                }

                if (allowPVE && !config.Settings.Maintained.IncludePVE && !allowPVP)
                {
                    Puts("Invalid configuration: Maintained Events -> Include PVE Bases is set false when all profiles have Allow PVP set to false. You can set Include PVE Bases to true, and Convert PVE To PVP to true.");
                }
            }

            if (config.Settings.Schedule.Enabled)
            {
                if (allowPVP && !config.Settings.Schedule.IncludePVP && !allowPVE)
                {
                    Puts("Invalid configuration: Scheduled Events -> Include PVP Bases is set false when all profiles have Allow PVP set to true. You can set Include PVP Bases to true, and Convert PVP To PVE to true.");
                }

                if (allowPVE && !config.Settings.Schedule.IncludePVE && !allowPVP)
                {
                    Puts("Invalid configuration: Scheduled Events -> Include PVE Bases is set false when all profiles have Allow PVP set to false. You can set Include PVE Bases to true, and Convert PVE To PVP to true.");
                }
            }

            LoadImportedSkins();
            yield return null;

            AllowBuyingPVP = config.Settings.Buyable.AllowBuyPVP;

            if (!AllowBuyingPVP && allProfilesPVP && config.Settings.Buyable.ConvertPVP)
            {
                AllowBuyingPVP = true;
            }

            if (config.RankedLadder.Amount > 0)
            {
                foreach (var record in config.RankedLadder.GetRecords())
                {
                    if (!permission.PermissionExists(record.Permission))
                    {
                        permission.RegisterPermission(record.Permission, this);
                    }
                    if (!permission.GroupExists(record.Group))
                    {
                        permission.CreateGroup(record.Group, record.Group, 0);
                        permission.GrantGroupPermission(record.Group, record.Permission, this);
                    }
                }
                yield return null;
            }

            using var values = config.Settings.Buyable.Wipe.All();
            foreach (var value in values)
            {
                if (value.Contains('.') && !permission.PermissionExists(value))
                {
                    permission.RegisterPermission(value, this);
                }
                if (!value.Contains('.') && !permission.GroupExists(value))
                {
                    permission.CreateGroup(value, value, 0);
                }
            }

            CheckForWipe(true);
            yield return null;
            RaidableModes.Clear();
            GetRaidableModes();

            if (user != null)
            {
                yield return LoadBaseTables(_sb, user);

                Reply(user, "Initialized base loot tables and profiles.");
            }

            RegisterLanguageMessages();
            yield return null;
            UpdateUI();
            yield return null;
            RegisterBuyableCooldownPermissions();
            yield return null;
        }

        private void RegisterBuyableCooldownPermissions()
        {
            using var keys = DisposableList<string>();
            foreach (var opt in config.Settings.Buyable.Cooldowns.Dictionary.Values)
            {
                keys.Clear();
                keys.AddRange(opt.Permissions.Keys);
                foreach (string key in keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        opt.Permissions.Remove(key);
                    }
                    else if (!key.Contains('.'))
                    {
                        continue;
                    }
                    else if (!permission.PermissionExists(key))
                    {
                        permission.RegisterPermission(key, this);
                    }
                }
            }
        }

        private bool AllowBuyingPVP = true;

        private IEnumerator ReloadProfiles(IPlayer user)
        {
            using var sb = DisposableBuilder.Get();
            yield return LoadProfiles(sb, user);

            foreach (var raid in Raids)
            {
                raid.Options = raid.rb.options;
            }

            InvalidatePasteData();
        }

        private IEnumerator ReloadTables(IPlayer user, bool edit = false, bool test = false, bool loot = false)
        {
            using var sb = DisposableBuilder.Get();
            yield return LoadTables(sb, user, edit, test, loot);
        }

        private IEnumerator ReloadTables(IPlayer user, DisposableBuilder sb, bool edit = false, bool test = false, bool loot = false)
        {
            yield return LoadTables(sb, user, edit, test, loot);
        }

        private void LoadImportedSkins()
        {
            string skinBoxFilename = Path.Combine(Name, "ImportedWorkshopSkins");
            try
            {
                ImportedWorkshopSkins = HarmonyDataLayer.ReadObject<SkinSettingsImportedWorkshop>(skinBoxFilename);
            }
            catch (Exception ex)
            {
                Puts(ex);
            }
            ImportedWorkshopSkins ??= new();
            ImportedWorkshopSkins.SkinList ??= new();
            string skinsFilename = Path.Combine(Name, "SkinsPlugin");
            try
            {
                skinsPlugin = HarmonyDataLayer.ReadObject<SkinsPlugin>(skinsFilename);
            }
            catch (Exception ex)
            {
                Puts(ex);
            }
            skinsPlugin ??= new();
            skinsPlugin.Skins ??= new();
        }

        protected void SaveProfile(string key, BuildingOptions options)
        {
            HarmonyDataLayer.WriteObject(Path.Combine(Name, "Profiles", key), options);
        }

        protected IEnumerator LoadTables(DisposableBuilder _sb, IPlayer user = null, bool edit = false, bool test = false, bool loot = false)
        {
            _sb.Length = 0;
            _sb.AppendLine("-");

            using var modes = GetRaidableModes().ToPooledList();
            modes.Add(RaidableMode.Random);

            foreach (string mode in modes)
            {
                string file = mode == RaidableMode.Random ? Path.Combine(Name, "Default_Loot") : Path.Combine(Name, "Difficulty_Loot", mode);
                if (!en && mode != RaidableMode.Random && !DataFileExists(file))
                {
                    file = Path.Combine(Name, "Difficulty_Loot", mode switch
                    {
                        "Легкий" => "Easy",
                        "Средний" => "Medium",
                        "Тяжело" => "Hard",
                        "Эксперт" => "Expert",
                        "Кошмарный" => "Nightmare",
                        _ => mode
                    });
                }
                try
                {
                    if (GetTable(file, out var lootList))
                    {
                        LoadTable(mode, _sb, file, Buildings.DifficultyLootLists[mode] = lootList, edit, test, loot);
                    }
                }
                catch (Exception ex)
                {
                    Puts("Error in file: {0} - {1}", file, ex);
                }
                yield return CoroutineEx.waitForFixedUpdate;
            }

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                string file = Path.Combine(Name, "Weekday_Loot", day.ToString());
                try
                {
                    if (GetTable(file, out var lootList))
                    {
                        LoadTable(RaidableMode.Disabled, _sb, file, Buildings.WeekdayLootLists[day] = lootList, edit, test, loot);
                    }
                }
                catch (Exception ex)
                {
                    Puts("Error in file: {0} - {1}", file, ex);
                }
                yield return CoroutineEx.waitForFixedUpdate;
            }

            yield return LoadBaseTables(_sb, user, edit);
        }

        protected IEnumerator LoadBaseTables(DisposableBuilder _sb, IPlayer user = null, bool edit = false, bool test = false, bool loot = false)
        {
            using var profiles = Buildings.Profiles.ToPooledList();
            profiles.Sort((x, y) => x.Value.Options.Level.CompareTo(y.Value.Options.Level));

            foreach (var (key, profile) in profiles)
            {
                string file = Path.Combine(Name, "Base_Loot", key);
                try
                {
                    if (GetTable(file, out var lootList))
                    {
                        LoadTable(profile.Options.Mode, _sb, file, profile.BaseLootList = lootList, edit, test, loot);
                    }
                }
                catch (Exception ex)
                {
                    Puts("Error in file: {0} - {1}", file, ex);
                }
                yield return CoroutineEx.waitForFixedUpdate;
            }

            if (!edit)
            {
                Puts("{0}", _sb.ToString());
            }
        }

        protected void ModifyDifficultyMode(IPlayer user, string mode, bool create, bool notice = true, string color = null, string profileName = null)
        {
            if (!create && config.Settings.Management.Chances.Dictionary.Count <= 1)
            {
                user.Reply($"You cannot remove all difficulties. There must be at least 1 at all times. Add a new difficulty, and then remove '{mode}' afterwards.");
                return;
            }

            using var worker = DisposableList<bool>();
            string lower = (en ? mode.ToLower() : mode).Replace(" ", "");

            worker.Add(create
                ? config.Settings.Management.Chances.TryAdd(mode, -1m)
                : config.Settings.Management.Chances.Remove(mode));

            worker.Add(create
                ? config.Settings.Management.TryAdd(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз", new())
                : config.Settings.Management.Remove(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз"));

            worker.Add(create
                ? config.Settings.Management.Amounts.TryAdd(mode, 1)
                : config.Settings.Management.Amounts.Remove(mode));

            worker.Add(create
                ? config.Settings.Management.Lockout.TryAdd(en
                      ? $"Time Between Raids In Minutes ({mode})"
                      : $"Время между рейдами в минутах ({mode})", 0.0)
                : config.Settings.Management.Lockout.Remove(en
                      ? $"Time Between Raids In Minutes ({mode})"
                      : $"Время между рейдами в минутах ({mode})"));

            worker.Add(create
                ? config.Settings.Management.Colors2.TryAdd(mode, color ?? Color2Settings.GetColor(config.Settings.Management.Colors2.Dictionary.Values))
                : config.Settings.Management.Colors2.Remove(mode));

            worker.Add(create
                ? config.Settings.Management.Colors1.TryAdd(mode, "000000")
                : config.Settings.Management.Colors1.Remove(mode));

            worker.Add(create
                ? config.Settings.Management.Players.TryAdd(mode, new())
                : config.Settings.Management.Players.Remove(mode));

            worker.Add(create
                ? config.Settings.Schedule.Wipe.TryAdd(mode, 0.0)
                : config.Settings.Schedule.Wipe.Remove(mode));

            worker.Add(create
                ? config.Settings.Economics.TryAdd(mode, 0.0)
                : config.Settings.Economics.Remove(mode));

            worker.Add(create
                ? config.Settings.ServerRewards.TryAdd(mode, 0)
                : config.Settings.ServerRewards.Remove(mode));

            worker.Add(create
                ? config.Settings.Buyable.Wipe.TryAdd(mode, BuyableWipeTime.Init($"raidablebases.buyraid.{lower}wipetime"))
                : config.Settings.Buyable.Wipe.Remove(mode));

            worker.Add(create
                ? config.Settings.Buyable.Limits.TryAdd(mode, 0)
                : config.Settings.Buyable.Limits.Remove(mode));

            worker.Add(create
                ? config.Settings.Buyable.Cooldowns.TryAdd(mode, new())
                : config.Settings.Buyable.Cooldowns.Remove(mode));

            worker.Add(create
                ? config.Settings.Maintained.Wipe.TryAdd(mode, 0.0)
                : config.Settings.Maintained.Wipe.Remove(mode));

            worker.Add(create
                ? config.RankedLadder.Points.TryAdd(mode, 0)
                : config.RankedLadder.Points.Remove(mode));

            worker.Add(create
                ? config.RankedLadder.Assign.TryAdd(mode, 0)
                : config.RankedLadder.Assign.Remove(mode));

            worker.Add(create
                ? config.RankedLadder.TryAdd(mode, new($"raidablebases.ladder.{lower}", $"raid{lower}", mode))
                : config.RankedLadder.Remove(mode));

            worker.Add(create
                ? config.UI.Buyable.TryAdd(en ? $"{mode} Button Color" : $"Цвет кнопки '{mode}'", "#497CAF")
                : config.UI.Buyable.Remove(en ? $"{mode} Button Color" : $"Цвет кнопки '{mode}'"));

            worker.Add(create
                ? config.UI.Buyable.TryAdd(en ? $"{mode} Text Color" : $"Цвет текста '{mode}'", "#FFFFFF")
                : config.UI.Buyable.Remove(en ? $"{mode} Text Color" : $"Цвет текста '{mode}'"));

            worker.Add(create
                ? config.Settings.Custom.TryAdd(mode, new() { new(0) })
                : config.Settings.Custom.Remove(mode));

            bool modified = worker.Exists(x => x);
            if (create)
            {
                if (notice)
                {
                    Reply(user, modified && !worker.All(x => x) ?
                        $"Difficulty '{mode}' has been updated with {worker.Count(x => x)} missing settings in the configuration file." : modified ?
                        $"Difficulty '{mode}' has been added to the configuration file. You may now edit the configuration, add copypaste files to the copypaste folder and profiles, and create loot tables." :
                        $"Difficulty '{mode}' exists already.");
                }
                if (modified)
                {
                    if (string.IsNullOrEmpty(profileName))
                    {
                        profileName = mode.Contains("Bases", CompareOptions.OrdinalIgnoreCase) ? mode : $"{mode} Bases";
                    }
                    if (!Buildings.Profiles.TryGetValue(profileName, out var profile))
                    {
                        Buildings.Profiles[profileName] = profile = new(this);
                        profile.ProfileName = profileName;
                        profile.Options.Mode = mode;
                        profile.Options.Level = GetRaidableModes().Count;
                        TryAddConfig(mode, profile);
                    }
                    foreach (var (key, other) in Buildings.Profiles)
                    {
                        if (other.Options.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
                        {
                            other.Options.Enabled = true;
                            SaveProfile(key, other.Options);
                        }
                    }
                    if (!RaidableModes.Contains(mode))
                    {
                        RaidableModes.Add(mode);
                        RegisterLanguageMessages();
                    }
                    if (!arguments.Contains(mode))
                    {
                        arguments.Add(mode);
                    }
                    SaveConfig();
                    if (DataFileExists(Path.Combine(Name, "Difficulty_Loot", mode)))
                    {
                        return;
                    }
                    if (DataFileExists(Path.Combine(Name, "Base_Loot", mode)))
                    {
                        return;
                    }
                    if (notice)
                    {
                        Reply(user, en ?
                            $"REMINDER: Make certain that you create Base_Loot and/or Difficulty_Loot tables for '{mode}'" :
                            $"НАПОМИНАНИЕ: Убедитесь, что вы создали таблицы Base_Loot и/или Difficulty_Loot для '{mode}'");
                    }
                }
            }
            else
            {
                Reply(user, modified ? $"{mode} has been removed." : $"{mode} does not exist.");
                if (modified)
                {
                    bool disabled = false;
                    foreach (var (key, profile) in Buildings.Profiles)
                    {
                        if (profile.Options.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
                        {
                            if (profile.Options.Enabled)
                            {
                                profile.Options.Enabled = false;
                                disabled = true;
                            }
                            SaveProfile(key, profile.Options);
                        }
                    }
                    if (disabled)
                    {
                        Reply(user, mx("Difficulty Disabled", user.Id, mode));
                    }
                    SaveConfig();
                }
            }
        }

        private static void TryAddConfig(string mode, BaseProfile profile)
        {
            if (mode.Equals(RaidableMode.Legacy, StringComparison.OrdinalIgnoreCase))
            {
                profile.Options.ArenaWalls.Enabled = false;
                profile.Options.ProtectionRadii.Set(15f);
                profile.Options.NPC.SpawnAmountScientists = 0;
                Puts($"{mode} difficulty has been preconfigured: No arena walls, protection radius of 15m, and disabled scientist NPC.");
            }
            if (mode.Equals("Underwater", StringComparison.OrdinalIgnoreCase))
            {
                profile.Options.ArenaWalls.Enabled = false;
                profile.Options.NPC.SpawnAmountScientists = 0;
                profile.Options.NPC.SpawnAmountMurderers = 0;
                profile.Options.Water.Seabed = 100f;
                profile.Options.Water.AllowSubmerged = true;
                profile.Options.Water.MaximumSeabedWaterDepth = -48.5f;
                Puts($"{mode} difficulty has been preconfigured: No arena walls, no npcs, 100% water spawn, and maximum depth at 48.5 meters.");
            }
            if (mode.Equals("Water", StringComparison.OrdinalIgnoreCase) || mode.Contains("Ship", StringComparison.OrdinalIgnoreCase))
            {
                profile.Options.ArenaWalls.Enabled = false;
                profile.Options.NPC.SpawnAmountScientists = 0;
                profile.Options.NPC.SpawnAmountMurderers = 0;
                profile.Options.Water.Seabed = 100f;
                profile.Options.Water.AllowSubmerged = true;
                profile.Options.Water.Surface = true;
                profile.Options.Water.IgnoreFlatTerrain = true;
                Puts($"{mode} difficulty has been preconfigured: No arena walls, no npcs, 100% water spawn, and to spawn on surface of water.");
            }
            if (mode.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            {
                profile.Options.ArenaWalls.Enabled = false;
                profile.Options.NPC.SpawnAmountScientists = 0;
                profile.Options.NPC.SpawnAmountMurderers = 0;
                profile.Options.Setup.DespawnLimit = 1;
                profile.Options.Setup.PasteHeightAdjustment = 100f;
                profile.Options.Setup.Sky = true;
                Puts($"{mode} difficulty has been preconfigured: No arena walls, no npcs, height increased by 100m, and sky placement enabled (precise ground placement skipped, height measured above the highest ground under the base).");
            }
            if (mode.Equals("Siege", StringComparison.OrdinalIgnoreCase))
            {
                profile.Options.Siege.Only = true;
                profile.Options.BlockOutsideDamageToBaseInside = true;
                profile.Options.BlockOutsideDamageToPlayersInside = true;
                Puts($"{mode} difficulty has been preconfigured: Siege weapons only, and no damage is allowed from outside of the event to the base or players.");
            }
        }

        protected void CommandDifficulty(IPlayer user, string command, string[] args)
        {
            if (!user.IsAdmin || !user.HasPermission("raidablebases.config")) { Reply(user, "No Permission"); return; }
            else if (IsGridLoading()) Reply(user, "GridIsLoading");
            else if (args.Length > 1 && args[0].Equals("add", StringComparison.OrdinalIgnoreCase)) ModifyDifficultyMode(user, args[1], true);
            else if (args.Length > 1 && args[0].Equals("remove", StringComparison.OrdinalIgnoreCase)) ModifyDifficultyMode(user, args[1], false);
            else ReplyOrLog(user, "Syntax: rb.difficulty add|remove \"name\"");
        }

        private void LoadTable(string mode, DisposableBuilder _sb, string file, List<LootItem> lootList, bool edit, bool test, bool loot)
        {
            if (lootList.Count == 0)
            {
                return;
            }

            bool zero = lootList.All(ti => ti.probability == 0f);
            bool stack = lootList.All(ti => ti.stacksize == 0);

            lootList.ForEach(ti =>
            {
                if (zero) ti.probability = 1f;
                if (stack) ti.stacksize = -1;
                ti.InitializeArmorSlots();
            });

            if (edit)
            {
                lootList.RemoveAll(ti =>
                {
                    if (RequiresOwnership(ti.definition, ti.skin))
                    {
                        _sb.Append(ti.shortname).Append(", ");
                        if (loot && DlcReplacements.TryGetValue(ti.shortname, out string r))
                        {
                            LootItem other = lootList.Find(x => x.shortname == r);
                            if (other != null)
                            {
                                if (other.definition?.stackable == 1) return true;
                                other.amount += ti.amount;
                                return true;
                            }
                            ti.shortname = r;
                            return false;
                        }
                        return true;
                    }
                    return false;
                });
                if (_sb.Length > 3)
                {
                    _sb.Length -= 2;
                }
            }

            if (!test)
            {
                HarmonyDataLayer.WriteObject(file, lootList);
            }

            //var probs = new Dictionary<float, int>();

            lootList.RemoveAll(ti =>
            {
                if (ti.amount == 0 || string.IsNullOrWhiteSpace(ti.shortname) || BlacklistedItems.Contains(ti.shortname))
                {
                    return true;
                }
                //if (!probs.ContainsKey(ti.probability))
                //{
                //    probs[ti.probability] = 0;
                //}
                //probs[ti.probability]++;
                if (ti.amount < ti.amountMin)
                {
                    ti.amount = ti.amountMin;
                }
                if (ti.shortname == "chocholate")
                {
                    ti.shortname = "chocolate";
                }
                if (ti.shortname.EndsWith(".bp"))
                {
                    ti.shortname = ti.shortname.Replace(".bp", "");
                    ti.isBlueprint = true;
                }
                return false;
            });

            if (lootList.Count == 0)
            {
                return;
            }

            //if (probs.Count > 0)
            //{
            //    _sb.Append(file);
            //    _sb.Append(string.Join("\n", probs.OrderBy(x => x.Key).Select(x => $"probability {x.Key} ({x.Value}x)")));
            //    _sb.AppendLine();
            //}

            if (!edit) _sb.AppendLine($"Loaded {lootList.Count} items from {file}");

            Buildings.LootID[mode] = DateTime.Now;

            HarmonyModInterface.Mods.CallHook("OnRaidableTableLoaded", file, lootList.Count, JsonConvert.SerializeObject(lootList));
        }

        private DateTime GetCurrentSessionLoot(string mode, List<Dictionary<string, object>> dictionary)
        {
            if (!Buildings.DifficultyLootLists.TryGetValue(mode, out var loot) || loot == null)
                return DateTime.MinValue;

            foreach (LootItem ti in loot)
            {
                Dictionary<string, object> d = new()
                {
                    ["shortname"] = ti.shortname,
                    ["amountMin"] = ti.amountMin,
                    ["amount"] = ti.amount,
                    ["skin"] = ti.skin,
                    ["blueprint"] = ti.isBlueprint,
                    ["probability"] = ti.probability,
                    ["stacksize"] = ti.stacksize,
                };

                if (ti.slots != null)
                {
                    Dictionary<string, object> slot = new()
                    {
                        ["min"] = ti.slots.min,
                        ["max"] = ti.slots.max,
                    };
                    d["slots"] = slot;
                }

                dictionary.Add(d);
            }

            return Buildings.LootID.GetValueOrDefault(mode);
        }


        private List<string> BlacklistedItems = new()
        {
            "ammo.snowballgun", "habrepair", "minihelicopter.repair", "scraptransport.repair", "vehicle.chassis", "vehicle.chassis.4mod", "vehicle.chassis.2mod", "vehicle.module", "car.key", "mlrs", "attackhelicopter",
            "scraptransportheli.repair", "snowmobile", "snowmobiletomaha", "submarineduo", "submarinesolo", "locomotive", "wagon", "workcart", "rhib", "rowboat", "tugboat", "door.key", "blueprintbase", "photo"
        };

        private bool GetTable(string file, out List<LootItem> lootList)
        {
            try
            {
                lootList = HarmonyDataLayer.ReadObject<List<LootItem>>(file);
            }
            catch (JsonReaderException ex)
            {
                Puts("Json error in loot table file: {0}\nUse a json validator: www.jsonlint.com\n\n{1}", file, ex);
                lootList = null;
                return false;
            }

            lootList ??= new();
            lootList.RemoveAll(ti => ti == null || string.IsNullOrWhiteSpace(ti.shortname));

            return lootList.Count > 0;
        }

        #endregion

    }
}
